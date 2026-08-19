using ChartPilot.Core.Manifests;
using YamlDotNet.RepresentationModel;

namespace ChartPilot.Core.Tests.Contracts;

public class ManifestNavigatorTests
{
    private const string DeploymentYaml = """
        apiVersion: apps/v1
        kind: Deployment
        metadata:
          name: member-api
          namespace: member-platform
          labels:
            app.kubernetes.io/name: member-api
            app.kubernetes.io/part-of: member-platform
          annotations:
            owner: platform-team
        spec:
          replicas: 3
          template:
            metadata:
              labels:
                app: member-api
            spec:
              serviceAccountName: member-api
              automountServiceAccountToken: false
              initContainers:
                - name: migrate
                  image: ghcr.io/example/migrate:1.0.0
              containers:
                - name: api
                  image: ghcr.io/example/member-api:1.4.2
                  securityContext:
                    runAsNonRoot: true
                - name: sidecar
                  image: ghcr.io/example/sidecar:2.0.0
        """;

    private const string CronJobYaml = """
        apiVersion: batch/v1
        kind: CronJob
        metadata:
          name: nightly
        spec:
          jobTemplate:
            spec:
              template:
                spec:
                  containers:
                    - name: job
                      image: busybox:1.36
        """;

    private static RenderedResource Load(string yaml, string kind, string name)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        var root = stream.Documents[0].RootNode;
        return new RenderedResource("apps/v1", kind, name, null, "chart/templates/x.yaml", root, yaml);
    }

    [Fact]
    public void Get_resolves_nested_paths_and_indexers()
    {
        var resource = Load(DeploymentYaml, "Deployment", "member-api");

        Assert.Equal("member-api", ManifestNavigator.GetString(resource.Root, "metadata.name"));
        Assert.Equal(3, ManifestNavigator.GetInt(resource.Root, "spec.replicas"));
        Assert.Equal(
            "ghcr.io/example/sidecar:2.0.0",
            ManifestNavigator.GetString(resource.Root, "spec.template.spec.containers[1].image"));
    }

    [Fact]
    public void Get_returns_null_for_missing_or_malformed_paths()
    {
        var resource = Load(DeploymentYaml, "Deployment", "member-api");

        Assert.Null(ManifestNavigator.Get(resource.Root, "spec.does.not.exist"));
        Assert.Null(ManifestNavigator.Get(resource.Root, "spec.template.spec.containers[9]"));
        Assert.Null(ManifestNavigator.Get(resource.Root, "spec.replicas.nope"));
        Assert.Null(ManifestNavigator.Get(resource.Root, "spec.template.spec.containers[abc]"));
        Assert.Null(ManifestNavigator.Get(null, "spec"));
    }

    [Fact]
    public void Get_with_an_empty_path_returns_the_node_itself()
    {
        var resource = Load(DeploymentYaml, "Deployment", "member-api");

        Assert.Same(resource.Root, ManifestNavigator.Get(resource.Root, string.Empty));
    }

    [Fact]
    public void GetBool_understands_yaml_booleans()
    {
        var resource = Load(DeploymentYaml, "Deployment", "member-api");

        Assert.False(ManifestNavigator.GetBool(resource.Root, "spec.template.spec.automountServiceAccountToken"));
        Assert.True(ManifestNavigator.GetBool(resource.Root, "spec.template.spec.containers[0].securityContext.runAsNonRoot"));
        Assert.Null(ManifestNavigator.GetBool(resource.Root, "spec.template.spec.containers[1].securityContext.runAsNonRoot"));
    }

    [Fact]
    public void GetContainers_returns_containers_then_init_containers_with_paths()
    {
        var resource = Load(DeploymentYaml, "Deployment", "member-api");

        var containers = ManifestNavigator.GetContainers(resource);

        Assert.Equal(3, containers.Count);
        Assert.Equal("api", containers[0].Name);
        Assert.Equal("spec.template.spec.containers[0]", containers[0].YamlPath);
        Assert.False(containers[0].IsInitContainer);
        Assert.Equal("spec.template.spec.containers[1]", containers[1].YamlPath);
        Assert.Equal("migrate", containers[2].Name);
        Assert.Equal("spec.template.spec.initContainers[0]", containers[2].YamlPath);
        Assert.True(containers[2].IsInitContainer);
    }

    [Fact]
    public void GetPodSpec_handles_cronjob_nesting()
    {
        var cronJob = Load(CronJobYaml, "CronJob", "nightly");

        Assert.Equal("spec.jobTemplate.spec.template.spec", ManifestNavigator.PodSpecPath(cronJob));
        Assert.NotNull(ManifestNavigator.GetPodSpec(cronJob));
        Assert.Equal("spec.jobTemplate.spec.template.spec.containers[0]", ManifestNavigator.GetContainers(cronJob)[0].YamlPath);
    }

    [Fact]
    public void GetPodSpec_of_a_bare_pod_is_spec()
    {
        const string yaml = """
            apiVersion: v1
            kind: Pod
            metadata:
              name: solo
            spec:
              containers:
                - name: app
                  image: nginx:1.27
            """;

        var pod = Load(yaml, "Pod", "solo");

        Assert.Equal("spec", ManifestNavigator.PodSpecPath(pod));
        Assert.Equal("spec.containers[0]", ManifestNavigator.GetContainers(pod)[0].YamlPath);
    }

    [Fact]
    public void GetContainers_is_empty_when_there_is_no_pod_spec()
    {
        const string yaml = """
            apiVersion: v1
            kind: ConfigMap
            metadata:
              name: settings
            data:
              key: value
            """;

        Assert.Empty(ManifestNavigator.GetContainers(Load(yaml, "ConfigMap", "settings")));
    }

    [Fact]
    public void Labels_and_annotations_are_read_from_metadata()
    {
        var resource = Load(DeploymentYaml, "Deployment", "member-api");

        Assert.Equal("member-api", ManifestNavigator.GetLabels(resource)["app.kubernetes.io/name"]);
        Assert.Equal("platform-team", ManifestNavigator.GetAnnotations(resource)["owner"]);
        Assert.Equal("member-api", ManifestNavigator.GetStringMap(resource.Root, "spec.template.metadata.labels")["app"]);
        Assert.Empty(ManifestNavigator.GetStringMap(resource.Root, "metadata.nothing"));
    }

    [Fact]
    public void GetSequence_returns_empty_for_non_sequences()
    {
        var resource = Load(DeploymentYaml, "Deployment", "member-api");

        Assert.Equal(2, ManifestNavigator.GetSequence(resource.Root, "spec.template.spec.containers").Count);
        Assert.Empty(ManifestNavigator.GetSequence(resource.Root, "spec.replicas"));
        Assert.Empty(ManifestNavigator.GetSequence(resource.Root, "spec.missing"));
    }

    [Fact]
    public void Null_scalars_read_as_null()
    {
        const string yaml = """
            apiVersion: v1
            kind: ConfigMap
            metadata:
              name: empties
            data:
              blank:
              explicit: null
              tilde: ~
              quoted: ""
            """;

        var resource = Load(yaml, "ConfigMap", "empties");

        Assert.Null(ManifestNavigator.GetString(resource.Root, "data.blank"));
        Assert.Null(ManifestNavigator.GetString(resource.Root, "data.explicit"));
        Assert.Null(ManifestNavigator.GetString(resource.Root, "data.tilde"));
        Assert.Equal(string.Empty, ManifestNavigator.GetString(resource.Root, "data.quoted"));
    }
}

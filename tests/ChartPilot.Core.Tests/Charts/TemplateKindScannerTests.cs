using ChartPilot.Core.Charts;

namespace ChartPilot.Core.Tests.Charts;

public class TemplateKindScannerTests
{
    [Fact]
    public void Detects_the_kind_of_a_heavily_templated_manifest()
    {
        const string template = """
            {{- if .Values.enabled }}
            {{- $fullName := include "chart.fullname" . }}
            apiVersion: apps/v1
            kind: Deployment
            metadata:
              name: {{ $fullName }}
              labels:
                {{- include "chart.labels" . | nindent 4 }}
            spec:
              replicas: {{ .Values.replicaCount }}
              template:
                spec:
                  containers:
                    - name: app
                      image: "{{ .Values.image.repository }}:{{ .Values.image.tag }}"
            {{- end }}
            """;

        Assert.Equal(new[] { "Deployment" }, TemplateKindScanner.Scan(template));
    }

    [Fact]
    public void Returns_every_kind_in_a_multi_document_template_sorted_and_distinct()
    {
        const string template = """
            apiVersion: rbac.authorization.k8s.io/v1
            kind: ClusterRoleBinding
            roleRef:
              kind: ClusterRole
              name: importer
            subjects:
              - kind: ServiceAccount
                name: importer
            ---
            apiVersion: v1
            kind: Service
            metadata:
              name: importer
            ---
            apiVersion: v1
            kind: Service
            metadata:
              name: importer-headless
            """;

        Assert.Equal(new[] { "ClusterRoleBinding", "Service" }, TemplateKindScanner.Scan(template));
    }

    [Fact]
    public void Ignores_commented_kinds()
    {
        const string template = """
            apiVersion: v1
            kind: ConfigMap
            # kind: Secret
            data:
              key: value
            """;

        Assert.Equal(new[] { "ConfigMap" }, TemplateKindScanner.Scan(template));
    }

    [Fact]
    public void Ignores_a_kind_that_only_appears_inside_a_template_expression()
    {
        const string template = """
            apiVersion: v1
            kind: {{ .Values.resourceKind }}
            metadata:
              name: generated
            """;

        Assert.Empty(TemplateKindScanner.Scan(template));
    }

    [Fact]
    public void Ignores_documents_without_an_apiVersion()
    {
        const string template = """
            kind: Deployment
            metadata:
              name: not-a-manifest
            """;

        Assert.Empty(TemplateKindScanner.Scan(template));
    }

    [Fact]
    public void Survives_a_helper_file_that_is_not_valid_yaml()
    {
        const string template = """
            {{/*
            Common labels
            */}}
            {{- define "chart.labels" -}}
            app.kubernetes.io/name: {{ include "chart.name" . }}
            {{- end -}}
            """;

        Assert.Empty(TemplateKindScanner.Scan(template));
    }

    [Fact]
    public void Survives_a_multi_line_template_expression_that_never_closes_on_its_line()
    {
        const string template = """
            apiVersion: v1
            kind: ConfigMap
            data:
              config: {{ include "chart.config"
                           (dict "root" . "extra" true) | quote }}
            """;

        Assert.Equal(new[] { "ConfigMap" }, TemplateKindScanner.Scan(template));
    }

    [Fact]
    public void Accepts_a_quoted_kind_value()
    {
        const string template = """
            apiVersion: v1
            kind: "ServiceAccount"
            metadata:
              name: importer
            """;

        Assert.Equal(new[] { "ServiceAccount" }, TemplateKindScanner.Scan(template));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t\n")]
    public void Empty_input_yields_no_kinds(string? template)
    {
        Assert.Empty(TemplateKindScanner.Scan(template));
    }

    [Fact]
    public void A_missing_file_yields_no_kinds_rather_than_throwing()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Charts", "does-not-exist.yaml");

        Assert.Empty(TemplateKindScanner.ScanFile(path));
    }
}

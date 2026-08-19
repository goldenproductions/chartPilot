// The monaco-yaml language worker.
// It is re-exported from a local module on purpose: referencing
// `monaco-yaml/yaml.worker` directly makes Vite serve the dependency
// untransformed, which breaks the worker in dev (documented in monaco-yaml's
// "Why doesn't it work with Vite?" note).
import 'monaco-yaml/yaml.worker.js';

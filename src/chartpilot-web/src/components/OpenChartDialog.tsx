import { useEffect, useState } from "react";
import { useDirectoryListing } from "../api/queries";
import { InlineError } from "./common";

const SUGGESTIONS = [
  "samples/charts/member-api",
  "samples/charts/legacy-importer",
  "samples/charts/batch-report",
];

/**
 * A browser cannot hand a server a real directory path — neither a file input nor the File System
 * Access API exposes one — so the folder tree is walked server side through GET /browse, confined
 * to the allowlist root. The path can still be typed or pasted; browsing just fills the same field.
 */
export function OpenChartDialog({
  initialPath,
  isSubmitting,
  error,
  allowlistRoot,
  onSubmit,
  onClose,
}: {
  initialPath: string | null;
  isSubmitting: boolean;
  error: unknown;
  allowlistRoot?: string | null;
  onSubmit: (chartPath: string) => void;
  onClose: () => void;
}) {
  const [path, setPath] = useState(initialPath ?? "");
  const [isBrowsing, setIsBrowsing] = useState(false);
  const [browsePath, setBrowsePath] = useState<string | null>(null);

  const listing = useDirectoryListing(browsePath, isBrowsing);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        onClose();
      }
    };

    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [onClose]);

  const submit = (value: string) => {
    const trimmed = value.trim();
    if (trimmed.length > 0 && !isSubmitting) {
      onSubmit(trimmed);
    }
  };

  // Browsing starts wherever the typed path points, so the button continues from what is in the
  // field rather than sending the user back to the root.
  const startBrowsing = () => {
    setBrowsePath(path.trim().length > 0 ? path.trim() : null);
    setIsBrowsing(true);
  };

  const navigateTo = (next: string) =>
    setBrowsePath(next.length > 0 ? next : null);

  const data = listing.data;

  return (
    <div
      className="overlay"
      role="dialog"
      aria-modal="true"
      aria-label="Open a Helm chart"
    >
      <div className="dialog" style={{ width: "min(680px, 100%)" }}>
        <header>
          <h2>Open a Helm chart</h2>
        </header>

        <form
          onSubmit={(event) => {
            event.preventDefault();
            submit(path);
          }}
        >
          <div className="content">
            <label
              className="field"
              htmlFor="chart-path"
              style={{ display: "block", marginBottom: 6 }}
            >
              Chart directory (the folder containing <code>Chart.yaml</code>)
            </label>

            <div style={{ display: "flex", gap: 8 }}>
              <input
                id="chart-path"
                type="text"
                value={path}
                autoFocus
                spellCheck={false}
                onChange={(event) => setPath(event.target.value)}
                style={{
                  flex: 1,
                  minWidth: 0,
                  maxWidth: "none",
                  fontFamily: "var(--font-mono)",
                }}
                placeholder="samples/charts/member-api"
              />
              <button
                type="button"
                className="btn"
                onClick={() =>
                  isBrowsing ? setIsBrowsing(false) : startBrowsing()
                }
                aria-expanded={isBrowsing}
              >
                {isBrowsing ? "Hide browser" : "Browse…"}
              </button>
            </div>

            {isBrowsing ? (
              <DirectoryBrowser
                listing={data}
                selectedPath={path.trim()}
                isLoading={listing.isLoading}
                isFetching={listing.isFetching}
                error={listing.error}
                onNavigate={navigateTo}
                onSelect={(selected) => setPath(selected)}
                onOpen={(selected) => {
                  setPath(selected);
                  submit(selected);
                }}
              />
            ) : (
              <div className="tag-row" style={{ marginTop: 8 }}>
                {SUGGESTIONS.map((suggestion) => (
                  <button
                    key={suggestion}
                    type="button"
                    className="tag"
                    onClick={() => setPath(suggestion)}
                  >
                    {suggestion}
                  </button>
                ))}
              </div>
            )}

            {allowlistRoot ? (
              <div
                style={{
                  marginTop: 8,
                  color: "var(--text-faint)",
                  fontSize: "var(--fs-sm)",
                }}
              >
                Paths are resolved under <code>{allowlistRoot}</code>.
              </div>
            ) : null}

            {error ? <InlineError error={error} /> : null}
          </div>

          <footer>
            <button type="button" className="btn" onClick={onClose}>
              Cancel
            </button>
            <button
              type="submit"
              className="btn btn-primary"
              disabled={isSubmitting || path.trim().length === 0}
            >
              {isSubmitting ? "Opening…" : "Open chart"}
            </button>
          </footer>
        </form>
      </div>
    </div>
  );
}

function DirectoryBrowser({
  listing,
  selectedPath,
  isLoading,
  isFetching,
  error,
  onNavigate,
  onSelect,
  onOpen,
}: {
  listing: import("../api/types").DirectoryListingDto | undefined;
  selectedPath: string;
  isLoading: boolean;
  isFetching: boolean;
  error: unknown;
  onNavigate: (path: string) => void;
  onSelect: (path: string) => void;
  onOpen: (path: string) => void;
}) {
  return (
    <div className="dir-browser" style={{ marginTop: 8 }}>
      <div className="dir-crumbs" aria-label="Breadcrumbs">
        {listing?.segments.map((segment, index) => (
          <span key={segment.path || "(root)"}>
            {index > 0 ? <span className="dir-crumb-sep">›</span> : null}
            <button
              type="button"
              className="dir-crumb"
              onClick={() => onNavigate(segment.path)}
            >
              {segment.name}
            </button>
          </span>
        ))}
        {isFetching && !isLoading ? (
          <span className="dir-crumb-sep">…</span>
        ) : null}
      </div>

      <ul className="dir-list" aria-label="Folders">
        {listing && !listing.isAllowlistRoot ? (
          <li>
            <button
              type="button"
              className="dir-entry"
              onClick={() => onNavigate(listing.parentPath ?? "")}
            >
              <span className="dir-icon" aria-hidden="true">
                ↰
              </span>
              <span className="dir-name">..</span>
            </button>
          </li>
        ) : null}

        {listing?.entries.map((entry) => {
          const isSelected = entry.isChart && entry.path === selectedPath;

          return (
            <li key={entry.path}>
              <button
                type="button"
                aria-current={isSelected ? true : undefined}
                className={[
                  "dir-entry",
                  entry.isChart ? "is-chart" : "",
                  isSelected ? "is-selected" : "",
                ]
                  .filter(Boolean)
                  .join(" ")}
                // A chart folder is a destination, not a waypoint: single click selects it, double
                // click opens it. Anything else navigates into it.
                onClick={() =>
                  entry.isChart ? onSelect(entry.path) : onNavigate(entry.path)
                }
                onDoubleClick={() =>
                  entry.isChart ? onOpen(entry.path) : onNavigate(entry.path)
                }
                title={entry.path}
              >
                <span className="dir-icon" aria-hidden="true">
                  {entry.isChart ? "⎈" : "▸"}
                </span>
                <span className="dir-name">{entry.name}</span>
                {entry.isChart ? (
                  <span className="dir-badge">Chart.yaml</span>
                ) : null}
              </button>
            </li>
          );
        })}

        {isLoading ? (
          <li className="dir-empty">Loading…</li>
        ) : error ? (
          <li className="dir-empty">
            <InlineError error={error} />
          </li>
        ) : listing && listing.entries.length === 0 ? (
          <li className="dir-empty">
            {listing.isChart
              ? "This folder is a chart. Open it, or step back up."
              : "No subfolders here."}
          </li>
        ) : null}
      </ul>

      {listing?.isChart && !listing.isAllowlistRoot ? (
        <div className="dir-current">
          <span className="dir-badge">Chart.yaml</span>
          <code>{listing.path}</code>
          <button
            type="button"
            className="btn btn-small"
            onClick={() => onOpen(listing.path)}
          >
            Open this folder
          </button>
        </div>
      ) : null}
    </div>
  );
}

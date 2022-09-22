﻿namespace Functionland.FxFiles.App.Components.Modal;
public class ArtifactSelectionResult
{
    public ArtifactSelectionResultType ResultType { get; set; }

    public IEnumerable<FsArtifact> SelectedArtifacts { get; set; } = default!;
}

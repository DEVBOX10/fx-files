﻿using Functionland.FxFiles.App.Components.Common;

namespace Functionland.FxFiles.App.Components
{
    public partial class ArtifactExplorer
    {
        [Parameter] public FsArtifact? CurrentArtifact { get; set; }
        [Parameter] public List<FsArtifact> Artifacts { get; set; } = new();
        [Parameter] public EventCallback<FsArtifact> OnArtifactsOptionsClick { get; set; } = new();
        [Parameter] public EventCallback<List<FsArtifact>> OnMultiArtifactsOptionsClick { get; set; } = new();
        [Parameter] public EventCallback<FsArtifact> OnSelectArtifact { get; set; } = new();
        [Parameter] public EventCallback OnCancelSelectDestionationMode { get; set; } = new();
        [Parameter] public ArtifactExplorerMode ArtifactExplorerMode { get; set; } = ArtifactExplorerMode.Normal;
        [Parameter] public ArtifactActionResult ArtifactActionResult { get; set; } = new();

        public List<FsArtifact> SelectedArtifacts { get; set; } = new List<FsArtifact>();
        public ViewModeEnum ViewMode = ViewModeEnum.list;
        public SortOrderEnum SortOrder = SortOrderEnum.asc;
        public bool IsSelected;
        public bool IsSelectedAll = false;
        public DateTimeOffset PointerDownTime;

        protected override Task OnInitAsync()
        {
            return base.OnInitAsync();
        }

        private async Task HandleArtifactOptionsClick(FsArtifact artifact)
        {
            await OnArtifactsOptionsClick.InvokeAsync(artifact);
        }

        private async Task HandleMultiArtifactsOptionsClick()
        {
            await OnMultiArtifactsOptionsClick.InvokeAsync(SelectedArtifacts);
        }

        private bool IsInRoot(FsArtifact? artifact)
        {
            return artifact is null ? true : false;
        }

        public void ToggleSortOrder()
        {
            if (SortOrder == SortOrderEnum.asc)
            {
                SortOrder = SortOrderEnum.desc;
            }
            else
            {
                SortOrder = SortOrderEnum.asc;
            }

            //todo: change order of list items
        }

        public void OnSortChange()
        {
            //todo: Open sort bottom sheet
        }

        public void ToggleSelectedAll()
        {
            IsSelectedAll = !IsSelectedAll;
            //todo: select all items
        }

        public void ChangeViewMode(ViewModeEnum mode)
        {
            ViewMode = mode;
        }

        public void CancelSelectionMode()
        {
            ArtifactExplorerMode = ArtifactExplorerMode.Normal;
            SelectedArtifacts = new List<FsArtifact>();
        }

        public async Task HandleCancelSelectDestionationMode()
        {
            await OnCancelSelectDestionationMode.InvokeAsync();
        }

        public void PointerDown()
        {
            PointerDownTime = DateTimeOffset.UtcNow;
        }

        public async Task PointerUp(FsArtifact artifact)
        {
            if (ArtifactExplorerMode == ArtifactExplorerMode.Normal)
            {
                var downTime = (DateTimeOffset.UtcNow.Ticks - PointerDownTime.Ticks) / TimeSpan.TicksPerMillisecond;
                if (downTime > 400)
                {
                    ArtifactExplorerMode = ArtifactExplorerMode.SelectArtifact;
                }
                else
                {
                    await OnSelectArtifact.InvokeAsync(artifact);
                }
            }
            else if(ArtifactExplorerMode == ArtifactExplorerMode.SelectDestionation)
            {
                await OnSelectArtifact.InvokeAsync(artifact);
            }
        }

        public void OnSelectionChanged(FsArtifact selectedArtifact)
        {
            if (SelectedArtifacts.Any(item => item.FullPath == selectedArtifact.FullPath))
            {
                SelectedArtifacts.Remove(selectedArtifact);
            }
            else
            {
                SelectedArtifacts.Add(selectedArtifact);
            }
        }

        public string GetArtifactIcon(FsArtifact artifact)
        {
            //todo: Proper icon for artifact
            return "text-file-icon";
        }

        public string GetArtifactSubText(FsArtifact artifact)
        {
            //todo: Proper subtext for artifact
            return "Modified 09/30/22";
        }
    }

}

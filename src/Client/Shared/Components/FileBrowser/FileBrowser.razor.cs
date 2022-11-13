﻿using Functionland.FxFiles.Client.Shared.Components.Common;
using Functionland.FxFiles.Client.Shared.Components.Modal;
using Functionland.FxFiles.Client.Shared.Services.Common;
using Functionland.FxFiles.Client.Shared.Utils;

using Prism.Events;

namespace Functionland.FxFiles.Client.Shared.Components;

public partial class FileBrowser
{
    // Modals
    private InputModal? _inputModalRef;
    private InputModal? _passwordModalRef;
    private ConfirmationModal? _confirmationModalRef;
    private FilterArtifactModal? _filteredArtifactModalRef;
    private SortArtifactModal? _sortedArtifactModalRef;
    private ArtifactOverflowModal? _artifactOverflowModalRef;
    private ArtifactSelectionModal? _artifactSelectionModalRef;
    private ConfirmationReplaceOrSkipModal? _confirmationReplaceOrSkipModalRef;
    private ArtifactDetailModal? _artifactDetailModalRef;
    private ProgressModal? _progressModalRef;
    private FxSearchInput? _fxSearchInputRef;
    private FileViewer? _fileViewerRef;
    private ExtractorBottomSheet? _extractorModalRef;

    // ProgressBar
    private string ProgressBarCurrentText { get; set; } = default!;
    private string ProgressBarCurrentSubText { get; set; } = default!;
    private int ProgressBarCurrentValue { get; set; }
    private int ProgressBarMax { get; set; }
    private CancellationTokenSource? _progressBarCts;
    private void ProgressBarOnCancel()
    {
        _progressBarCts?.Cancel();
    }

    // Search
    private DeepSearchFilter? SearchFilter { get; set; }
    private bool _isFileCategoryFilterBoxOpen = true;
    private bool _isInSearch;
    private bool _isPrePairForFirstTimeInSearch = true;
    private string _inlineSearchText = string.Empty;
    private string _searchText = string.Empty;
    private ArtifactDateSearchType? _artifactsSearchFilterDate;
    private ArtifactCategorySearchType? _artifactsSearchFilterType;

    private FsArtifact? _currentArtifactValue;
    private FsArtifact? CurrentArtifact
    {
        get => _currentArtifactValue;
        set
        {
            if (_currentArtifactValue == value)
                return;

            if (_currentArtifactValue is not null)
            {
                FileWatchService.UnWatchArtifact(_currentArtifactValue);
            }
            _currentArtifactValue = value;
            if (_currentArtifactValue is not null)
            {
                FileWatchService.WatchArtifact(_currentArtifactValue);
            }
            ArtifactState.CurrentMyDeviceArtifact = CurrentArtifact;
        }
    }

    private List<FsArtifact> _pins = new();
    private List<FsArtifact> _allArtifacts = new();
    private List<FsArtifact> _displayedArtifacts = new();
    private List<FsArtifact> _selectedArtifacts = new();
    private FileCategoryType? _fileCategoryFilter;

    private ArtifactExplorerMode _artifactExplorerModeValue;
    private ArtifactExplorerMode ArtifactExplorerMode
    {
        get => _artifactExplorerModeValue;
        set
        {
            if (_artifactExplorerModeValue == value)
                return;
            ArtifactExplorerModeChange(value);
        }
    }

    private SortTypeEnum _currentSortType = SortTypeEnum.Name;
    private bool _isAscOrder = true;
    private bool _isArtifactExplorerLoading = false;
    private bool _isPinBoxLoading = true;
    private bool _isGoingBack;

    [AutoInject] public IAppStateStore ArtifactState { get; set; } = default!;
    [AutoInject] public IEventAggregator EventAggregator { get; set; } = default!;
    [AutoInject] public IFileWatchService FileWatchService { get; set; } = default!;
    [AutoInject] public IZipService ZipService { get; set; } = default!;
    [AutoInject] public IntentHolder IntentHolder { get; set; } = default!;
    public SubscriptionToken ArtifactChangeSubscription { get; set; } = default!;

    [Parameter] public IPinService PinService { get; set; } = default!;
    [Parameter] public IFileService FileService { get; set; } = default!;
    [Parameter] public IArtifactThumbnailService<IFileService> ThumbnailService { get; set; } = default!;
    [Parameter] public string? DefaultPath { get; set; }

    protected override async Task OnInitAsync()
    {
        ArtifactChangeSubscription = EventAggregator
                           .GetEvent<ArtifactChangeEvent>()
                           .Subscribe(
                               HandleChangedArtifacts,
                               ThreadOption.BackgroundThread, keepSubscriberReferenceAlive: true);

        if (string.IsNullOrWhiteSpace(DefaultPath))
        {
            var preArtifact = ArtifactState.CurrentMyDeviceArtifact;
            CurrentArtifact = preArtifact;
        }
        else
        {
            var filePath = Path.GetDirectoryName(DefaultPath);
            var defaultArtifact = await FileService.GetArtifactAsync(filePath);
            CurrentArtifact = defaultArtifact;
        }

        _ = Task.Run(async () =>
        {
            await LoadPinsAsync();
            await InvokeAsync(StateHasChanged);
        });
        _ = Task.Run(async () =>
        {
            await LoadChildrenArtifactsAsync(CurrentArtifact);
            await InvokeAsync(StateHasChanged);
        });

        await base.OnInitAsync();

    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            HandleIntentReceiver();

            _ = EventAggregator
                           .GetEvent<IntentReceiveEvent>()
                           .Subscribe(
                               HandleIntentReceiver,
                               ThreadOption.BackgroundThread, keepSubscriberReferenceAlive: true);
        }
        if (_isInSearch && _isPrePairForFirstTimeInSearch)
        {
            await JSRuntime.InvokeVoidAsync("SearchInputFocus");
            _isPrePairForFirstTimeInSearch = false;
        }
        if (_isGoingBack)
        {
            _isGoingBack = false;
            await JSRuntime.InvokeVoidAsync("getLastScrollPosition");
        }
        await base.OnAfterRenderAsync(firstRender);
    }
    private void HandleProgressBar(string currentText)
    {
        ProgressBarCurrentValue++;
        ProgressBarCurrentSubText = $"{ProgressBarCurrentValue} of {ProgressBarMax}";
        ProgressBarCurrentText = currentText;
    }

    private void InitialProgressBar(int maxValue)
    {
        ProgressBarCurrentValue = 0;
        ProgressBarMax = maxValue;
        ProgressBarCurrentSubText = string.Empty;
        ProgressBarCurrentText = "Loading...";
    }

    public async Task HandleCopyArtifactsAsync(List<FsArtifact> artifacts)
    {
        try
        {
            List<FsArtifact> existArtifacts = new();
            var artifactActionResult = new ArtifactActionResult()
            {
                ActionType = ArtifactActionType.Copy,
                Artifacts = artifacts
            };

            var destinationPath = await HandleSelectDestinationAsync(artifactActionResult);

            if (string.IsNullOrWhiteSpace(destinationPath))
                return;

            if (_progressModalRef is not null)
            {
                InitialProgressBar(artifacts.Count);
                await _progressModalRef.ShowAsync(ProgressMode.Progressive, Localizer.GetString(AppStrings.CopyFiles), true);
            }
            _progressBarCts = new CancellationTokenSource();

            if (destinationPath == CurrentArtifact?.FullPath)
            {
                var desArtifacts = await FileService.GetArtifactsAsync(destinationPath).ToListAsync();

                foreach (var item in artifacts)
                {
                    if (item.ArtifactType == FsArtifactType.File)
                    {
                        var nameWithOutExtenstion = Path.GetFileNameWithoutExtension(item.FullPath);
                        var pathWithOutExtenstion = Path.Combine(item.ParentFullPath, nameWithOutExtenstion);
                        var oldArtifactPath = item.FullPath;

                        var copyText = " - Copy";

                        while (true)
                        {
                            var counter = 1;
                            var fullPathWithCopy = pathWithOutExtenstion + copyText;
                            fullPathWithCopy = Path.ChangeExtension(fullPathWithCopy, item.FileExtension);

                            if (desArtifacts.All(d => d.FullPath != fullPathWithCopy)) break;

                            counter++;
                            copyText += $" ({counter})";
                        }

                        var newArtifactPath = Path.ChangeExtension(pathWithOutExtenstion + copyText, item.FileExtension);

                        var fileStream = await FileService.GetFileContentAsync(oldArtifactPath);
                        await FileService.CreateFileAsync(newArtifactPath, fileStream);
                    }
                    else if (item.ArtifactType == FsArtifactType.Folder)
                    {
                        var oldArtifactPath = item.FullPath;
                        var oldArtifactParentPath = item.ParentFullPath;
                        var oldArtifactName = item.Name;

                        var copyText = " - Copy";

                        while (true)
                        {
                            var counter = 1;
                            var fullPathWithCopy = oldArtifactPath + copyText;

                            if (desArtifacts.All(d => d.FullPath != fullPathWithCopy)) break;

                            counter++;
                            copyText += $" ({counter})";
                        }

                        var newArtifactPath = oldArtifactPath + copyText;
                        var newArtifactName = oldArtifactName + copyText;
                        await FileService.CreateFolderAsync(oldArtifactParentPath, newArtifactName);
                        var oldArtifactChildren = await FileService.GetArtifactsAsync(oldArtifactPath).ToListAsync();
                        await FileService.CopyArtifactsAsync(oldArtifactChildren, newArtifactPath, false);
                    }
                    else
                    {
                        // ToDo : copy drive not supported, show proper message
                    }

                    HandleProgressBar(item.Name);
                }
            }
            else
            {
                try
                {
                    await FileService.CopyArtifactsAsync(artifacts, destinationPath, false,
                        onProgress: async (progressInfo) =>
                        {
                            ProgressBarCurrentText = progressInfo.CurrentText ?? string.Empty;
                            ProgressBarCurrentSubText = progressInfo.CurrentSubText ?? string.Empty;
                            ProgressBarCurrentValue = progressInfo.CurrentValue ?? 0;
                            ProgressBarMax = progressInfo.MaxValue ?? artifacts.Count;
                            await InvokeAsync(StateHasChanged);
                        }, cancellationToken: _progressBarCts.Token);

                }
                catch (CanNotOperateOnFilesException ex)
                {
                    existArtifacts = ex.FsArtifacts;
                }
                finally
                {
                    if (_progressModalRef is not null)
                    {
                        await _progressModalRef.CloseAsync();
                    }
                }

                var overwriteArtifacts = GetShouldOverwriteArtifacts(artifacts, existArtifacts); //TODO: we must enhance this

                if (existArtifacts.Count > 0)
                {
                    if (_confirmationReplaceOrSkipModalRef != null)
                    {
                        var result = await _confirmationReplaceOrSkipModalRef.ShowAsync(existArtifacts.Count);

                        if (result?.ResultType == ConfirmationReplaceOrSkipModalResultType.Replace)
                        {
                            _progressBarCts = new CancellationTokenSource();

                            if (_progressModalRef is not null)
                            {
                                await _progressModalRef.ShowAsync(ProgressMode.Progressive, Localizer.GetString(AppStrings.ReplacingFiles), true);

                                await FileService.CopyArtifactsAsync(overwriteArtifacts, destinationPath, true,
                                    onProgress: async (progressInfo) =>
                                    {
                                        ProgressBarCurrentText = progressInfo.CurrentText ?? string.Empty;
                                        ProgressBarCurrentSubText = progressInfo.CurrentSubText ?? string.Empty;
                                        ProgressBarCurrentValue = progressInfo.CurrentValue ?? 0;
                                        ProgressBarMax = progressInfo.MaxValue ?? artifacts.Count;
                                        await InvokeAsync(StateHasChanged);
                                    },
                                    cancellationToken: _progressBarCts.Token);

                                await _progressModalRef.CloseAsync();
                            }
                        }
                        ChangeDeviceBackFunctionality(ArtifactExplorerMode);
                    }
                }
            }

            var title = Localizer.GetString(AppStrings.TheCopyOpreationSuccessedTiltle);
            var message = Localizer.GetString(AppStrings.TheCopyOpreationSuccessedMessage);
            FxToast.Show(title, message, FxToastType.Success);

            await NavigateToDestination(destinationPath);
        }
        catch (Exception exception)
        {
            ExceptionHandler?.Handle(exception);
        }
        finally
        {
            if (_progressModalRef is not null)
            {
                await _progressModalRef.CloseAsync();
            }
            await CloseFileViewer();
        }
    }

    public async Task HandleMoveArtifactsAsync(List<FsArtifact> artifacts)
    {
        try
        {
            List<FsArtifact> existArtifacts = new();
            var artifactActionResult = new ArtifactActionResult()
            {
                ActionType = ArtifactActionType.Move,
                Artifacts = artifacts
            };

            var destinationPath = await HandleSelectDestinationAsync(artifactActionResult);
            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                return;
            }

            try
            {
                _progressBarCts = new CancellationTokenSource();

                if (_progressModalRef is not null)
                {
                    await _progressModalRef.ShowAsync(ProgressMode.Progressive, Localizer.GetString(AppStrings.MovingFiles), true);
                }

                await FileService.MoveArtifactsAsync(artifacts, destinationPath, false, onProgress: async (progressInfo) =>
                {
                    ProgressBarCurrentText = progressInfo.CurrentText ?? string.Empty;
                    ProgressBarCurrentSubText = progressInfo.CurrentSubText ?? string.Empty;
                    ProgressBarCurrentValue = progressInfo.CurrentValue ?? 0;
                    ProgressBarMax = progressInfo.MaxValue ?? artifacts.Count;
                    await InvokeAsync(StateHasChanged);
                },
                    cancellationToken: _progressBarCts.Token);
            }
            catch (CanNotOperateOnFilesException ex)
            {
                existArtifacts = ex.FsArtifacts;
            }
            finally
            {
                if (_progressModalRef is not null)
                {
                    await _progressModalRef.CloseAsync();
                }

                await CloseFileViewer();
            }

            var overwriteArtifacts = GetShouldOverwriteArtifacts(artifacts, existArtifacts); //TODO: we must enhance this

            if (existArtifacts.Count > 0)
            {
                if (_confirmationReplaceOrSkipModalRef is not null)
                {
                    var result = await _confirmationReplaceOrSkipModalRef.ShowAsync(existArtifacts.Count);
                    ChangeDeviceBackFunctionality(ArtifactExplorerMode);

                    if (result?.ResultType == ConfirmationReplaceOrSkipModalResultType.Replace)
                    {
                        _progressBarCts = new CancellationTokenSource();
                        if (_progressModalRef is not null)
                        {
                            await _progressModalRef.ShowAsync(ProgressMode.Progressive, Localizer.GetString(AppStrings.ReplacingFiles), true);
                        }

                        await FileService.MoveArtifactsAsync(overwriteArtifacts, destinationPath, true, onProgress: async (progressInfo) =>
                        {
                            ProgressBarCurrentText = progressInfo.CurrentText ?? string.Empty;
                            ProgressBarCurrentSubText = progressInfo.CurrentSubText ?? string.Empty;
                            ProgressBarCurrentValue = progressInfo.CurrentValue ?? 0;
                            ProgressBarMax = progressInfo.MaxValue ?? artifacts.Count;
                            await InvokeAsync(StateHasChanged);
                        },
                            cancellationToken: _progressBarCts.Token);
                    }
                }
            }

            ArtifactExplorerMode = ArtifactExplorerMode.Normal;

            var title = Localizer.GetString(AppStrings.TheMoveOpreationSuccessedTiltle);
            var message = Localizer.GetString(AppStrings.TheMoveOpreationSuccessedMessage);
            FxToast.Show(title, message, FxToastType.Success);

            await NavigateToDestination(destinationPath);
        }
        catch (Exception exception)
        {
            ExceptionHandler?.Handle(exception);
        }
        finally
        {
            if (_progressModalRef is not null)
            {
                await _progressModalRef.CloseAsync();
            }
        }
    }

    public async Task HandleRenameArtifactAsync(FsArtifact? artifact)
    {
        var result = await GetInputModalResult(artifact);
        if (result?.ResultType == InputModalResultType.Cancel)
        {
            return;
        }

        string? newName = result?.Result;

        if (artifact?.ArtifactType == FsArtifactType.Folder)
        {
            await RenameFolderAsync(artifact, newName);
        }
        else if (artifact?.ArtifactType == FsArtifactType.File)
        {
            await RenameFileAsync(artifact, newName);
        }
        else if (artifact?.ArtifactType == FsArtifactType.Drive)
        {
            var title = Localizer.GetString(AppStrings.ToastErrorTitle);
            var message = Localizer.GetString(AppStrings.RootfolderRenameException);
            FxToast.Show(title, message, FxToastType.Error);
        }
    }

    public async Task HandlePinArtifactsAsync(List<FsArtifact> artifacts)
    {
        try
        {
            _isPinBoxLoading = true;
            await PinService.SetArtifactsPinAsync(artifacts);
            await UpdatePinedArtifactsAsync(artifacts, true);
            if (_isInSearch)
            {
                CancelSelectionMode();
            }
        }
        catch (Exception exception)
        {
            ExceptionHandler?.Handle(exception);
        }
        finally
        {
            _isPinBoxLoading = false;
        }
    }

    public async Task HandleUnPinArtifactsAsync(List<FsArtifact> artifacts)
    {
        try
        {
            _isPinBoxLoading = true;
            var pathArtifacts = artifacts.Select(a => a.FullPath);
            await PinService.SetArtifactsUnPinAsync(pathArtifacts);
            await UpdatePinedArtifactsAsync(artifacts, false);
        }
        catch (Exception exception)
        {
            ExceptionHandler?.Handle(exception);
            _isPinBoxLoading = false;
        }
    }

    public async Task HandleDeleteArtifactsAsync(List<FsArtifact> artifacts)
    {
        try
        {
            if (_confirmationModalRef != null)
            {
                var result = new ConfirmationModalResult();

                if (artifacts.Count == 1)
                {
                    var singleArtifact = artifacts.SingleOrDefault();
                    result = await _confirmationModalRef.ShowAsync(Localizer.GetString(AppStrings.DeleteItems, singleArtifact?.Name), Localizer.GetString(AppStrings.DeleteItemDescription));
                    ChangeDeviceBackFunctionality(ArtifactExplorerMode);
                }
                else
                {
                    result = await _confirmationModalRef.ShowAsync(Localizer.GetString(AppStrings.DeleteItems, artifacts.Count), Localizer.GetString(AppStrings.DeleteItemsDescription));
                    ChangeDeviceBackFunctionality(ArtifactExplorerMode);
                }

                if (result.ResultType == ConfirmationModalResultType.Confirm)
                {
                    _progressBarCts = new CancellationTokenSource();
                    if (_progressModalRef is not null)
                    {
                        await _progressModalRef.ShowAsync(ProgressMode.Progressive, Localizer.GetString(AppStrings.DeletingFiles), true);

                        await FileService.DeleteArtifactsAsync(artifacts, onProgress: async (progressInfo) =>
                        {
                            ProgressBarCurrentText = progressInfo.CurrentText ?? string.Empty;
                            ProgressBarCurrentSubText = progressInfo.CurrentSubText ?? string.Empty;
                            ProgressBarCurrentValue = progressInfo.CurrentValue ?? 0;
                            ProgressBarMax = progressInfo.MaxValue ?? artifacts.Count;
                            await InvokeAsync(StateHasChanged);
                        }, cancellationToken: _progressBarCts.Token);

                        await _progressModalRef.CloseAsync();
                    }
                }
            }
        }
        catch (Exception exception)
        {
            ExceptionHandler?.Handle(exception);
        }

        finally
        {

            if (_progressModalRef is not null)
            {
                _progressBarCts?.Cancel();
                await _progressModalRef.CloseAsync();
            }
            await CloseFileViewer();
        }
    }

    public async Task HandleShowDetailsArtifact(List<FsArtifact> artifacts)
    {
        var isMultiple = artifacts.Count > 1;
        var isDrive = false;

        if (isMultiple is false)
        {
            isDrive = artifacts.SingleOrDefault()?.ArtifactType == FsArtifactType.Drive;
        }

        if (_artifactDetailModalRef is null)
            return;

        var result = await _artifactDetailModalRef.ShowAsync(artifacts, isMultiple, (isDrive || IsInRoot(CurrentArtifact)));
        ChangeDeviceBackFunctionality(ArtifactExplorerMode);

        switch (result.ResultType)
        {
            case ArtifactDetailModalResultType.Download:
                //TODO: Implement download logic here
                //await HandleDownloadArtifacts(artifact);
                break;
            case ArtifactDetailModalResultType.Move:
                await HandleMoveArtifactsAsync(artifacts);
                break;
            case ArtifactDetailModalResultType.Pin:
                await HandlePinArtifactsAsync(artifacts);
                break;
            case ArtifactDetailModalResultType.Unpin:
                await HandleUnPinArtifactsAsync(artifacts);
                break;
            case ArtifactDetailModalResultType.More:
                if (artifacts.Count > 1)
                {
                    await HandleSelectedArtifactsOptions(artifacts);
                }
                else
                {
                    await HandleOptionsArtifact(artifacts[0]);
                }
                break;
            case ArtifactDetailModalResultType.Upload:
                //TODO: Implement upload logic here
                break;
            case ArtifactDetailModalResultType.Close:
                if (artifacts.Count > 1)
                {
                    await HandleSelectedArtifactsOptions(artifacts);
                }
                else
                {
                    await HandleOptionsArtifact(artifacts[0]);
                }
                break;
            default:
                break;
        }
    }

    public async Task HandleCreateFolder(string path)
    {
        if (_inputModalRef is null) return;

        var createFolder = Localizer.GetString(AppStrings.CreateFolder);
        var newFolderPlaceholder = Localizer.GetString(AppStrings.NewFolderPlaceholder);

        var result = await _inputModalRef.ShowAsync(createFolder, string.Empty, string.Empty, newFolderPlaceholder);
        ChangeDeviceBackFunctionality(ArtifactExplorerMode);

        try
        {
            if (result?.ResultType == InputModalResultType.Confirm)
            {
                await FileService.CreateFolderAsync(path, result?.Result); //ToDo: Make CreateFolderAsync nullable         
            }
        }
        catch (Exception exception)
        {
            ExceptionHandler?.Handle(exception);
        }
    }

    public async Task HandleShareFiles(List<FsArtifact> artifacts)
    {
        _isArtifactExplorerLoading = true;
        StateHasChanged();
        var files = GetShareFiles(artifacts);

        await Share.Default.RequestAsync(new ShareMultipleFilesRequest
        {
            Title = "Share with app",
            Files = files
        });
        _isArtifactExplorerLoading = false;
    }

    public async Task HandleExtractArtifactAsync(FsArtifact zipArtifact, List<FsArtifact>? innerArtifacts = null, string? destinationDirectory = null)
    {
        var extractResult = new ExtractorBottomSheetResult();
        if (_inputModalRef is null)
        {
            return;
        }

        var folderName = Path.GetFileNameWithoutExtension(zipArtifact.Name);
        var createFolder = Localizer.GetString(AppStrings.FolderName);
        var newFolderPlaceholder = Localizer.GetString(AppStrings.ExtractFolderTargetNamePlaceHolder);
        var extractBtnTitle = Localizer.GetString(AppStrings.Extract);

        try
        {
            var result = await _inputModalRef.ShowAsync(createFolder, string.Empty, folderName, newFolderPlaceholder, extractBtnTitle);

            if (result?.ResultType == InputModalResultType.Cancel)
            {
                return;
            }

            var destinationFolderName = string.IsNullOrWhiteSpace(result?.Result) == false ? result.Result : folderName;

            destinationDirectory ??= zipArtifact.ParentFullPath;

            if (destinationDirectory != null)
            {
                if (_extractorModalRef == null)
                {
                    return;
                }
                extractResult = await _extractorModalRef.ShowAsync(zipArtifact.FullPath, destinationDirectory,
                    destinationFolderName, innerArtifacts);
            }

            if (destinationDirectory != null && extractResult.ExtractorResult == ExtractorBottomSheetResultType.Success)
            {
                var destinationPath = Path.Combine(destinationDirectory, destinationFolderName);
                await NavigateToDestination(destinationPath);
            }
        }
        catch (Exception exception)
        {
            ExceptionHandler?.Handle(exception);
        }
        finally
        {
            ChangeDeviceBackFunctionality(ArtifactExplorerMode);
        }

    }

    private List<ShareFile> GetShareFiles(List<FsArtifact> artifacts)
    {
        var files = new List<ShareFile>();
        foreach (var artifact in artifacts)
        {
            if (artifact.ArtifactType == FsArtifactType.File)
            {
                files.Add(new ShareFile(artifact.FullPath));
            }
        }

        return files;
    }

    private async Task LoadPinsAsync()
    {
        _isPinBoxLoading = true;

        try
        {
            _pins = await PinService.GetPinnedArtifactsAsync();
        }
        catch (Exception exception)
        {
            ExceptionHandler.Handle(exception);
        }
        finally
        {
            _isPinBoxLoading = false;
        }
    }

    private async Task LoadChildrenArtifactsAsync(FsArtifact? artifact = null)
    {
        try
        {
            _isArtifactExplorerLoading = true;

            var childrenArtifacts = FileService.GetArtifactsAsync(artifact?.FullPath);
            if (artifact is null)
            {
                GoBackService.OnInit(null, true, true);
            }
            else
            {
                GoBackService.OnInit(HandleToolbarBackClick, true, false);
            }

            var allFiles = FileService.GetArtifactsAsync(artifact?.FullPath);
            var artifacts = new List<FsArtifact>();
            await foreach (var item in childrenArtifacts)
            {
                item.IsPinned = await PinService.IsPinnedAsync(item);
                artifacts.Add(item);
            }

            _allArtifacts = artifacts;
            RefreshDisplayedArtifacts();
        }
        catch (Exception exception)
        {
            ExceptionHandler?.Handle(exception);
        }
        finally
        {
            _isArtifactExplorerLoading = false;
        }
    }

    private bool IsInRoot(FsArtifact? artifact)
    {
        return artifact is null ? true : false;
    }

    private async Task HandleSelectArtifactAsync(FsArtifact artifact)
    {
        _fxSearchInputRef?.HandleClearInputText();
        if (artifact.ArtifactType == FsArtifactType.File)
        {
            var isOpened = await _fileViewerRef?.OpenArtifact(artifact);

            if (isOpened == false)
            {
#if BlazorHybrid
                try
                {

                    if (DeviceInfo.Current.Platform == DevicePlatform.iOS || DeviceInfo.Current.Platform == DevicePlatform.macOS || DeviceInfo.Current.Platform == DevicePlatform.MacCatalyst)
                    {
                        var uri = new Uri($"file://{artifact.FullPath}");
                        await Launcher.OpenAsync(uri);

                    }
                    else
                    {
                        await Launcher.OpenAsync(new OpenFileRequest
                        {
                            File = new ReadOnlyFile(artifact?.FullPath)
                        });
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    ExceptionHandler?.Handle(new DomainLogicException(Localizer.GetString(nameof(AppStrings.ArtifactUnauthorizedAccessException))));
                }
                catch (Exception exception)
                {
                    ExceptionHandler?.Handle(exception);
                }

                if (_isInSearch)
                {
                    CancelSearch(true);
                    CurrentArtifact = artifact;
                    await LoadChildrenArtifactsAsync(CurrentArtifact);
                }
#endif
            }
        }
        else
        {
            if (_isInSearch)
            {
                CancelSearch(true);
            }

            await OpenFolderAsync(artifact);
        }
    }

    private async Task OpenFolderAsync(FsArtifact artifact)
    {
        try
        {
            if (_isInSearch)
            {
                CancelSearch(true);
            }
            else
            {
                await JSRuntime.InvokeVoidAsync("saveScrollPosition");
                _isGoingBack = false;
            }

            CurrentArtifact = artifact;
            _displayedArtifacts = new();

            _ = Task.Run(async () =>
            {
                await LoadChildrenArtifactsAsync(CurrentArtifact);
                await InvokeAsync(() => StateHasChanged());
            });
        }
        catch (Exception exception)
        {
            ExceptionHandler?.Handle(exception);
        }
    }

    private async Task HandleOptionsArtifact(FsArtifact artifact)
    {
        ArtifactOverflowResult? result = null;
        if (_artifactOverflowModalRef is not null)
        {
            var pinOptionResult = new PinOptionResult()
            {
                IsVisible = true,
                Type = artifact.IsPinned == true ? PinOptionResultType.Remove : PinOptionResultType.Add
            };
            var isDrive = artifact?.ArtifactType == FsArtifactType.Drive;
            var isVisibleShareWithApp = artifact?.ArtifactType == FsArtifactType.File;
            result = await _artifactOverflowModalRef!.ShowAsync(false, pinOptionResult, isVisibleShareWithApp, artifact?.FileCategory, isDrive);
            ChangeDeviceBackFunctionality(ArtifactExplorerMode);
        }

        if (artifact == null)
        {
            return;
        }

        switch (result?.ResultType)
        {
            case ArtifactOverflowResultType.Details:
                try
                {
                    _isArtifactExplorerLoading = true;
                    await HandleShowDetailsArtifact(new List<FsArtifact> { artifact });
                }
                catch (Exception exception)
                {
                    ExceptionHandler?.Handle(exception);
                }
                finally
                {
                    _isArtifactExplorerLoading = false;
                }
                break;
            case ArtifactOverflowResultType.Rename:
                await HandleRenameArtifactAsync(artifact);
                break;
            case ArtifactOverflowResultType.Copy:
                await HandleCopyArtifactsAsync(new List<FsArtifact> { artifact });
                break;
            case ArtifactOverflowResultType.Pin:
                await HandlePinArtifactsAsync(new List<FsArtifact> { artifact });
                break;
            case ArtifactOverflowResultType.UnPin:
                await HandleUnPinArtifactsAsync(new List<FsArtifact> { artifact });
                break;
            case ArtifactOverflowResultType.Move:
                await HandleMoveArtifactsAsync(new List<FsArtifact> { artifact });
                break;
            case ArtifactOverflowResultType.ShareWithApp:
                await HandleShareFiles(new List<FsArtifact> { artifact });
                break;
            case ArtifactOverflowResultType.Delete:
                await HandleDeleteArtifactsAsync(new List<FsArtifact> { artifact });
                break;
            case ArtifactOverflowResultType.Extract:
                await HandleExtractArtifactAsync(artifact);
                break;
            case ArtifactOverflowResultType.Cancel:
                break;
            case null:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public async Task ToggleSelectedAll()
    {
        if (ArtifactExplorerMode == ArtifactExplorerMode.Normal)
        {
            ArtifactExplorerMode = ArtifactExplorerMode.SelectArtifact;
            _selectedArtifacts = new List<FsArtifact>();
            foreach (var artifact in _displayedArtifacts)
            {
                artifact.IsSelected = true;
                _selectedArtifacts.Add(artifact);
            }
        }
    }

    public void ChangeViewMode()
    {
        var viewMode = ArtifactState.ViewMode == ViewModeEnum.List ? ViewModeEnum.Grid : ViewModeEnum.List;
        ArtifactState.ViewMode = viewMode;
        StateHasChanged();
    }

    public void CancelSelectionMode()
    {
        foreach (var artifact in _selectedArtifacts)
        {
            artifact.IsSelected = false;
        }
        _selectedArtifacts.Clear();
        ArtifactExplorerMode = ArtifactExplorerMode.Normal;
    }

    private async Task HandleSelectedArtifactsOptions(List<FsArtifact> artifacts)
    {
        var selectedArtifactsCount = artifacts.Count;
        var isMultiple = selectedArtifactsCount > 1;

        if (selectedArtifactsCount <= 0) return;

        ArtifactOverflowResult? result = null;
        if (_artifactOverflowModalRef is not null)
        {
            ArtifactExplorerMode = ArtifactExplorerMode.SelectArtifact;
            var pinOptionResult = GetPinOptionResult(artifacts);
            var isVisibleShareWithApp = !artifacts.Any(a => a.ArtifactType != FsArtifactType.File);

            var firstArtifactType = artifacts.FirstOrDefault()?.FileCategory;
            FileCategoryType? fileCategoryType = artifacts.All(x => x.FileCategory == firstArtifactType) ? firstArtifactType : null;

            result = await _artifactOverflowModalRef.ShowAsync(isMultiple, pinOptionResult, isVisibleShareWithApp, fileCategoryType, IsInRoot(CurrentArtifact));
            ChangeDeviceBackFunctionality(ArtifactExplorerMode);
        }

        switch (result?.ResultType)
        {
            case ArtifactOverflowResultType.Details:
                try
                {
                    _isArtifactExplorerLoading = true;
                    await HandleShowDetailsArtifact(artifacts);
                }
                catch (Exception exception)
                {
                    ExceptionHandler?.Handle(exception);
                }
                finally
                {
                    _isArtifactExplorerLoading = false;
                }
                break;
            case ArtifactOverflowResultType.Rename when (!isMultiple):
                var singleArtifact = artifacts.SingleOrDefault();
                await HandleRenameArtifactAsync(singleArtifact);
                break;
            case ArtifactOverflowResultType.Copy:
                await HandleCopyArtifactsAsync(artifacts);
                break;
            case ArtifactOverflowResultType.Pin:
                await HandlePinArtifactsAsync(artifacts);
                break;
            case ArtifactOverflowResultType.UnPin:
                await HandleUnPinArtifactsAsync(artifacts);
                break;
            case ArtifactOverflowResultType.Move:
                await HandleMoveArtifactsAsync(artifacts);
                break;
            case ArtifactOverflowResultType.Delete:
                await HandleDeleteArtifactsAsync(artifacts);
                break;
            case ArtifactOverflowResultType.ShareWithApp:
                await HandleShareFiles(artifacts);
                break;
            case ArtifactOverflowResultType.Extract:
                await HandleExtractArtifactAsync(artifacts.First());
                break;
            case ArtifactOverflowResultType.Cancel:
                break;
        }
    }

    private void ArtifactExplorerModeChange(ArtifactExplorerMode mode)
    {
        ChangeDeviceBackFunctionality(mode);
        _artifactExplorerModeValue = mode;

        if (mode == ArtifactExplorerMode.Normal)
        {
            CancelSelectionMode();
        }

        StateHasChanged();
    }

    private PinOptionResult GetPinOptionResult(List<FsArtifact> artifacts)
    {
        if (artifacts.All(a => a.IsPinned == true))
        {
            return new PinOptionResult()
            {
                IsVisible = true,
                Type = PinOptionResultType.Remove
            };
        }
        else if (artifacts.All(a => a.IsPinned == false))
        {
            return new PinOptionResult()
            {
                IsVisible = true,
                Type = PinOptionResultType.Add
            };
        }

        return new PinOptionResult()
        {
            IsVisible = false,
            Type = null
        };
    }

    private async Task<InputModalResult?> GetInputModalResult(FsArtifact? artifact)
    {
        string artifactType = "";

        if (artifact?.ArtifactType == FsArtifactType.File)
        {
            artifactType = Localizer.GetString(AppStrings.FileRenamePlaceholder);
        }
        else if (artifact?.ArtifactType == FsArtifactType.Folder)
        {
            artifactType = Localizer.GetString(AppStrings.FolderRenamePlaceholder);
        }
        else
        {
            return null;
        }

        var Name = Path.GetFileNameWithoutExtension(artifact.Name);

        InputModalResult? result = null;
        if (_inputModalRef is not null)
        {
            result = await _inputModalRef.ShowAsync(Localizer.GetString(AppStrings.ChangeName), Localizer.GetString(AppStrings.Rename).ToString().ToUpper(), Name, artifactType);
            ChangeDeviceBackFunctionality(ArtifactExplorerMode);
        }

        return result;
    }

    private async Task<string?> HandleSelectDestinationAsync(ArtifactActionResult artifactActionResult)
    {
        if (_artifactSelectionModalRef is null)
            return null;

        var sourceArtifact = CurrentArtifact;
        if (artifactActionResult.Artifacts?.Count == 1)
        {
            var sourceArtifactPath = artifactActionResult.Artifacts.FirstOrDefault()?.FullPath;
            if (sourceArtifactPath == CurrentArtifact?.FullPath)
            {
                sourceArtifact = await FileService.GetArtifactAsync(CurrentArtifact?.ParentFullPath);
            }
        }

        var result = await _artifactSelectionModalRef.ShowAsync(sourceArtifact, artifactActionResult);
        ChangeDeviceBackFunctionality(ArtifactExplorerMode);

        string? destinationPath = null;

        if (result?.ResultType == ArtifactSelectionResultType.Ok)
        {
            var destinationFsArtifact = result.SelectedArtifacts.FirstOrDefault();
            destinationPath = destinationFsArtifact?.FullPath;
        }

        return destinationPath;
    }

    private readonly SemaphoreSlim _semaphoreArtifactChanged = new SemaphoreSlim(1);
    private async void HandleChangedArtifacts(ArtifactChangeEvent artifactChangeEvent)
    {
        try
        {
            await _semaphoreArtifactChanged.WaitAsync();

            if (artifactChangeEvent.FsArtifact == null) return;

            if (artifactChangeEvent.ChangeType == FsArtifactChangesType.Add)
            {
                _ = UpdateAddedArtifactAsync(artifactChangeEvent.FsArtifact);
            }
            else if (artifactChangeEvent.ChangeType == FsArtifactChangesType.Delete)
            {
                _ = UpdateRemovedArtifactAsync(artifactChangeEvent.FsArtifact);
            }
            else if (artifactChangeEvent.ChangeType == FsArtifactChangesType.Rename && artifactChangeEvent.Description != null)
            {
                _ = UpdateRenamedArtifactAsync(artifactChangeEvent.FsArtifact, artifactChangeEvent.Description);
            }
            else if (artifactChangeEvent.ChangeType == FsArtifactChangesType.Modify)
            {
                _ = UpdateModefiedArtifactAsync(artifactChangeEvent.FsArtifact);
            }
        }
        catch (Exception exp)
        {
            ExceptionHandler.Handle(exp);
        }
        finally
        {
            _semaphoreArtifactChanged.Release();
        }
    }

    private async Task UpdateAddedArtifactAsync(FsArtifact artifact)
    {
        try
        {
            if (artifact.ParentFullPath != CurrentArtifact?.FullPath) return;

            _allArtifacts.Add(artifact);
            RefreshDisplayedArtifacts();
            await InvokeAsync(() => StateHasChanged());
        }
        catch (Exception ex)
        {
            ExceptionHandler.Handle(ex);
        }
    }

    private async Task UpdateRemovedArtifactAsync(FsArtifact artifact)
    {
        try
        {
            if (artifact.FullPath == CurrentArtifact?.FullPath)
            {
                await HandleToolbarBackClick();
            }
            else
            {
                _allArtifacts.RemoveAll(a => a.FullPath == artifact.FullPath);
                RefreshDisplayedArtifacts();
            }
            await InvokeAsync(() => StateHasChanged());
        }
        catch (Exception ex)
        {
            ExceptionHandler.Handle(ex);
        }
    }

    private async Task UpdateModefiedArtifactAsync(FsArtifact artifact)
    {
        try
        {
            var modefiedArtifact = _allArtifacts.Where(a => a.FullPath == artifact.FullPath).FirstOrDefault();
            if (modefiedArtifact == null) return;

            modefiedArtifact.Size = artifact.Size;
            modefiedArtifact.LastModifiedDateTime = artifact.LastModifiedDateTime;

            RefreshDisplayedArtifacts();
            await InvokeAsync(() => StateHasChanged());

        }
        catch (Exception ex)
        {
            ExceptionHandler.Handle(ex);
        }
    }

    private async Task UpdateRenamedArtifactAsync(FsArtifact artifact, string oldFullPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(oldFullPath)) return;

            FsArtifact? artifactRenamed = null;

            if (CurrentArtifact?.FullPath == oldFullPath)
            {
                CurrentArtifact.FullPath = artifact.FullPath;
                CurrentArtifact.Name = artifact.Name;
                await OpenFolderAsync(CurrentArtifact);
            }
            else
            {
                artifactRenamed = _allArtifacts.Where(a => a.FullPath == oldFullPath).FirstOrDefault();
                if (artifactRenamed != null)
                {
                    artifactRenamed.FullPath = artifact.FullPath;
                    artifactRenamed.Name = artifact.Name;
                    artifactRenamed.FileExtension = artifact.FileExtension;
                    RefreshDisplayedArtifacts();
                    await InvokeAsync(() => StateHasChanged());
                }
            }
        }
        catch (Exception ex)
        {
            ExceptionHandler.Handle(ex);
        }
    }

    private async Task UpdatePinedArtifactsAsync(IEnumerable<FsArtifact> artifacts, bool IsPinned)
    {
        await LoadPinsAsync();
        var artifactPath = artifacts.Select(a => a.FullPath);

        if (CurrentArtifact != null && artifactPath.Any(p => p == CurrentArtifact.FullPath))
        {
            CurrentArtifact.IsPinned = IsPinned;
        }
        else
        {
            foreach (var artifact in _allArtifacts)
            {
                if (artifactPath.Contains(artifact.FullPath))
                {
                    artifact.IsPinned = IsPinned;
                }
            }
            RefreshDisplayedArtifacts();
        }
    }

    private async Task HandleCancelInLineSearchAsync()
    {
        ArtifactExplorerMode = ArtifactExplorerMode.Normal;
        _inlineSearchText = string.Empty;
        await LoadChildrenArtifactsAsync(CurrentArtifact);
    }

    private void HandleSearchFocused()
    {
        _isInSearch = true;
        _displayedArtifacts.Clear();
    }

    CancellationTokenSource? searchCancellationTokenSource;

    private async Task HandleSearchAsync(string text)
    {
        CancelSelectionMode();
        _isFileCategoryFilterBoxOpen = false;
        _isArtifactExplorerLoading = true;
        _searchText = text;
        ApplySearchFilter(text, _artifactsSearchFilterDate, _artifactsSearchFilterType);
        if (string.IsNullOrWhiteSpace(SearchFilter.SearchText) && SearchFilter.ArtifactDateSearchType == null && SearchFilter.ArtifactCategorySearchType == null)
        {
            CancelSearch();
            _isArtifactExplorerLoading = false;
            return;
        }
        _allArtifacts.Clear();
        _displayedArtifacts.Clear();

        RefreshDisplayedArtifacts();

        searchCancellationTokenSource?.Cancel();

        searchCancellationTokenSource = new CancellationTokenSource();
        var token = searchCancellationTokenSource.Token;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        await Task.Run(async () =>
        {
            var buffer = new List<FsArtifact>();
            try
            {
                await foreach (var item in FileService.GetSearchArtifactAsync(SearchFilter, token).WithCancellation(token))
                {
                    if (token.IsCancellationRequested)
                        return;

                    _allArtifacts.Add(item);
                    if (sw.ElapsedMilliseconds <= 1000)
                        continue;

                    if (token.IsCancellationRequested)
                        return;

                    RefreshDisplayedArtifacts();
                    await InvokeAsync(() =>
                    {
                        if (_displayedArtifacts.Count > 0 && _isArtifactExplorerLoading)
                        {
                            _isArtifactExplorerLoading = false;
                        }
                        StateHasChanged();
                    });
                    sw.Restart();
                    await Task.Yield();
                }

                if (token.IsCancellationRequested)
                    return;

                RefreshDisplayedArtifacts();
                await InvokeAsync(StateHasChanged);
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                _isArtifactExplorerLoading = false;
            }

        }, token);
    }

    private void ApplySearchFilter(string searchText, ArtifactDateSearchType? date = null, ArtifactCategorySearchType? type = null)
    {
        SearchFilter ??= new DeepSearchFilter();
        SearchFilter.SearchText = !string.IsNullOrWhiteSpace(searchText) ? searchText : string.Empty;
        SearchFilter.ArtifactCategorySearchType = type ?? null;
        SearchFilter.ArtifactDateSearchType = date ?? null;
    }

    private void HandleInLineSearch(string text)
    {
        if (text != null)
        {
            _inlineSearchText = text;
            RefreshDisplayedArtifacts();
        }
    }

    private async Task HandleToolbarBackClick()
    {
        _searchText = string.Empty;
        _inlineSearchText = string.Empty;
        _fxSearchInputRef?.HandleClearInputText();

        switch (ArtifactExplorerMode)
        {
            case ArtifactExplorerMode.Normal:
                if (_isInSearch)
                {
                    CancelSearch(true);
                    _ = Task.Run(async () =>
                    {
                        await LoadChildrenArtifactsAsync(CurrentArtifact);
                        await InvokeAsync(StateHasChanged);
                    });
                    return;
                }
                _fxSearchInputRef?.HandleClearInputText();
                await UpdateCurrentArtifactForBackButton(CurrentArtifact);
                _ = Task.Run(async () =>
                {
                    await LoadChildrenArtifactsAsync(CurrentArtifact);
                    await InvokeAsync(StateHasChanged);
                });
                await JSRuntime.InvokeVoidAsync("OnScrollEvent");
                _isGoingBack = true;
                break;

            case ArtifactExplorerMode.SelectArtifact:
                ArtifactExplorerMode = ArtifactExplorerMode.Normal;
                break;

            case ArtifactExplorerMode.SelectDestionation:
                ArtifactExplorerMode = ArtifactExplorerMode.Normal;
                break;

            default:
                break;
        }
    }

    private async Task UpdateCurrentArtifactForBackButton(FsArtifact? fsArtifact)
    {
        try
        {
            CurrentArtifact = await FileService.GetArtifactAsync(fsArtifact?.ParentFullPath);
        }
        catch (DomainLogicException ex) when (ex is ArtifactPathNullException)
        {
            CurrentArtifact = null;
        }
    }

    private void RefreshDisplayedArtifacts(
        bool applyInlineSearch = true,
        bool applyFilters = true,
        bool applySort = true)
    {
        IEnumerable<FsArtifact> displayingArtifacts = _allArtifacts;

        if (applyInlineSearch)
        {
            displayingArtifacts = ApplyInlineSearch(displayingArtifacts);
        }

        if (applyFilters)
        {
            displayingArtifacts = ApplyFilters(displayingArtifacts);
        }

        if (applySort)
        {
            displayingArtifacts = ApplySort(displayingArtifacts);
        }

        _displayedArtifacts = displayingArtifacts.ToList();
    }

    private IEnumerable<FsArtifact> ApplyInlineSearch(IEnumerable<FsArtifact> artifacts)
    {
        return (string.IsNullOrEmpty(_inlineSearchText) || string.IsNullOrWhiteSpace(_inlineSearchText))
            ? artifacts
            : artifacts.Where(a => a.Name.ToLower().Contains(_inlineSearchText.ToLower()));
    }

    private IEnumerable<FsArtifact> ApplyFilters(IEnumerable<FsArtifact> artifacts)
    {
        return _fileCategoryFilter is null
            ? artifacts
            : artifacts.Where(fa =>
            {
                if (_fileCategoryFilter == FileCategoryType.Document)
                {
                    return (fa.FileCategory == FileCategoryType.Document
                                                || fa.FileCategory == FileCategoryType.Pdf
                                                || fa.FileCategory == FileCategoryType.Other);
                }
                return fa.FileCategory == _fileCategoryFilter;
            });
    }

    private IEnumerable<FsArtifact> ApplySort(IEnumerable<FsArtifact> artifacts)
    {
        return SortDisplayedArtifacts(artifacts);
    }

    private async Task HandleFilterClick()
    {
        if (_isArtifactExplorerLoading || _filteredArtifactModalRef is null)
            return;

        _fileCategoryFilter = await _filteredArtifactModalRef.ShowAsync();
        ChangeDeviceBackFunctionality(ArtifactExplorerMode);
        await JSRuntime.InvokeVoidAsync("OnScrollEvent");
        _isArtifactExplorerLoading = true;
        await Task.Run(() =>
        {
            RefreshDisplayedArtifacts();
        });
        _isArtifactExplorerLoading = false;

    }

    // TODO: septate variable for sort display variable and sort variable use case
    private async Task HandleSortOrderClick()
    {
        if (_isArtifactExplorerLoading) return;

        _isAscOrder = !_isAscOrder;
        _isArtifactExplorerLoading = true;
        await Task.Delay(100);
        try
        {
            var sortedDisplayArtifact = SortDisplayedArtifacts(_displayedArtifacts);
            _displayedArtifacts = new();
            _displayedArtifacts = sortedDisplayArtifact.ToList();

            // For smooth transition and time for the animation to complete
            await Task.Delay(100);
        }
        catch (Exception exception)
        {
            ExceptionHandler.Handle(exception);
        }
        finally
        {
            _isArtifactExplorerLoading = false;
        }
    }

    private async Task HandleSortClick()
    {
        if (_isArtifactExplorerLoading) return;

        _currentSortType = await _sortedArtifactModalRef!.ShowAsync();
        ChangeDeviceBackFunctionality(ArtifactExplorerMode);
        _isArtifactExplorerLoading = true;
        StateHasChanged();
        try
        {
            var sortedDisplayArtifact = SortDisplayedArtifacts(_displayedArtifacts);
            _displayedArtifacts = sortedDisplayArtifact.ToList();
        }
        catch (Exception exception)
        {
            ExceptionHandler.Handle(exception);
        }
        finally
        {
            _isArtifactExplorerLoading = false;
        }
    }

    private IEnumerable<FsArtifact> SortDisplayedArtifacts(IEnumerable<FsArtifact> artifacts)
    {
        IEnumerable<FsArtifact> sortedArtifactsQuery;
        if (_currentSortType is SortTypeEnum.LastModified)
        {
            if (_isAscOrder)
            {
                sortedArtifactsQuery = artifacts.OrderBy(artifact => artifact.ArtifactType != FsArtifactType.Folder).ThenBy(artifact => artifact.LastModifiedDateTime);
            }
            else
            {
                sortedArtifactsQuery = artifacts.OrderByDescending(artifact => artifact.ArtifactType == FsArtifactType.Folder).ThenByDescending(artifact => artifact.LastModifiedDateTime);
            }

        }

        else if (_currentSortType is SortTypeEnum.Size)
        {
            if (_isAscOrder)
            {
                sortedArtifactsQuery = artifacts.OrderBy(artifact => artifact.ArtifactType != FsArtifactType.Folder).ThenBy(artifact => artifact.Size);
            }
            else
            {
                sortedArtifactsQuery = artifacts.OrderByDescending(artifact => artifact.ArtifactType == FsArtifactType.Folder).ThenByDescending(artifact => artifact.Size);
            }
        }

        else if (_currentSortType is SortTypeEnum.Name)
        {
            if (_isAscOrder)
            {
                sortedArtifactsQuery = artifacts.OrderBy(artifact => artifact.ArtifactType != FsArtifactType.Folder).ThenBy(artifact => artifact.Name);
            }
            else
            {
                sortedArtifactsQuery = artifacts.OrderByDescending(artifact => artifact.ArtifactType == FsArtifactType.Folder).ThenByDescending(artifact => artifact.Name);
            }
        }
        else
        {
            sortedArtifactsQuery = artifacts.OrderBy(artifact => artifact.ArtifactType != FsArtifactType.Folder).ThenBy(artifact => artifact.Name);
        }

        return sortedArtifactsQuery;
    }

    private async Task RenameFileAsync(FsArtifact artifact, string? newName)
    {
        try
        {
            await FileService.RenameFileAsync(artifact.FullPath, newName);
        }
        catch (Exception exception)
        {
            ExceptionHandler?.Handle(exception);
        }
    }

    private async Task RenameFolderAsync(FsArtifact artifact, string? newName)
    {
        try
        {
            await FileService.RenameFolderAsync(artifact.FullPath, newName);
        }
        catch (Exception exception)
        {
            ExceptionHandler?.Handle(exception);
        }
    }

    private static List<FsArtifact> GetShouldOverwriteArtifacts(List<FsArtifact> artifacts, List<FsArtifact> existArtifacts)
    {
        List<FsArtifact> overwriteArtifacts = new();
        var pathExistArtifacts = existArtifacts.Select(a => a.FullPath);
        foreach (var artifact in artifacts)
        {
            if (pathExistArtifacts.Any(p => p.StartsWith(artifact.FullPath)))
            {
                overwriteArtifacts.Add(artifact);
            }
        }

        return overwriteArtifacts;
    }

    private async Task NavigateToDestination(string? destinationPath)
    {
        if (_isInSearch)
        {
            CancelSearch(true);
        }
        if (!string.IsNullOrWhiteSpace(_inlineSearchText))
        {
            _fxSearchInputRef?.HandleClearInputText();
            await HandleCancelInLineSearchAsync();
        }

        CurrentArtifact = await FileService.GetArtifactAsync(destinationPath);
        _ = LoadChildrenArtifactsAsync(CurrentArtifact);
        _ = LoadPinsAsync();
    }

    private void ChangeDeviceBackFunctionality(ArtifactExplorerMode mode)
    {
        if (mode == ArtifactExplorerMode.SelectArtifact)
        {
            GoBackService.OnInit((Task () =>
            {
                CancelSelectionMode();
                return Task.CompletedTask;
            }), true, false);
        }
        else if (mode == ArtifactExplorerMode.Normal)
        {
            if (CurrentArtifact == null && _isInSearch is false)
            {
                GoBackService.OnInit(null, true, true);
            }
            else
            {
                if (_isInSearch)
                {
                    GoBackService.OnInit((Task () =>
                    {
                        CancelSearch();
                        return Task.CompletedTask;
                    }), true, false);
                }
                else
                {
                    GoBackService.OnInit((async Task () =>
                    {
                        await HandleToolbarBackClick();
                        await Task.CompletedTask;
                    }), true, false);
                }
            }
        }
    }

    private void ChangeFileCategoryFilterMode()
    {
        _isFileCategoryFilterBoxOpen = !_isFileCategoryFilterBoxOpen;
    }

    private async Task ChangeArtifactsSearchFilterDate(ArtifactDateSearchType? date)
    {
        CancelSearch();
        _artifactsSearchFilterDate = _artifactsSearchFilterDate == date ? null : date;
        await HandleSearchAsync(_searchText);
    }

    private async Task ChangeArtifactsSearchFilterType(ArtifactCategorySearchType? type)
    {
        CancelSearch();
        _artifactsSearchFilterType = _artifactsSearchFilterType == type ? null : type;
        await HandleSearchAsync(_searchText);
    }

    private void CancelSearch(bool shouldExist = false)
    {
        searchCancellationTokenSource?.Cancel();
        SearchFilter = null;
        _fxSearchInputRef?.HandleClearInputText();
        _displayedArtifacts.Clear();
        CancelSelectionMode();
        _isInSearch = shouldExist is false;
        if (!shouldExist)
            return;

        _artifactsSearchFilterType = null;
        _artifactsSearchFilterDate = null;
        _isPrePairForFirstTimeInSearch = true;
        _isFileCategoryFilterBoxOpen = true;
    }

    private async Task NavigateArtifactForShowInFolder(FsArtifact artifact)
    {
        if (artifact.ArtifactType == FsArtifactType.File)
        {
            var destinationArtifact = await FileService.GetArtifactAsync(artifact.ParentFullPath);
            CurrentArtifact = destinationArtifact;
            await HandleSelectArtifactAsync(destinationArtifact);
        }
        else
        {
            CurrentArtifact = artifact;
            await HandleSelectArtifactAsync(artifact);
        }
    }

    private async Task CloseFileViewer()
    {
        if (_fileViewerRef is not null && _fileViewerRef.IsModalOpen)
        {
            await _fileViewerRef.HandleBackAsync();
        }
    }

    private void HandleIntentReceiver(IntentReceiveEvent? intentReceiveEvent = null)
    {
        if (IntentHolder.FileUrl is null || _fileViewerRef is null)
            return;

        var artifact = FileService.GetArtifactAsync(IntentHolder.FileUrl).GetAwaiter().GetResult();
        CurrentArtifact = artifact;
        IntentHolder.FileUrl = null;
        _ = _fileViewerRef.OpenArtifact(artifact);
    }

    private async Task FileViewerBack()
    {
        if (CurrentArtifact?.ParentFullPath is not null && CurrentArtifact.ArtifactType == FsArtifactType.File)
        {
            var artifact = await FileService.GetArtifactAsync(CurrentArtifact.ParentFullPath);
            CurrentArtifact = artifact;
        }
        await OnInitAsync();
    }

}
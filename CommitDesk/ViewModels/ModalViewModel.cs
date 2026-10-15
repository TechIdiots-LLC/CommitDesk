using CommunityToolkit.Mvvm.ComponentModel;

namespace CommitDesk.ViewModels;

/// <summary>A view model shown in a modal page that completes with a result.</summary>
public abstract partial class ModalViewModel<T> : ObservableObject
{
    // Run the caller's continuation later, not inside the button click that completed the dialog.
    readonly TaskCompletionSource<T> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<T> Result => _result.Task;

    /// <summary>Raised when the page should close.</summary>
    public event Action? CloseRequested;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _error;

    protected abstract T CancelledResult { get; }

    protected void Complete(T value)
    {
        if (_result.TrySetResult(value)) CloseRequested?.Invoke();
    }

    /// <summary>Called by the page when it is dismissed any other way.</summary>
    public void Cancel() => Complete(CancelledResult);
}

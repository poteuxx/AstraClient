namespace AstraClient.Mvvm;

/// <summary>Base class for page-level view models.</summary>
public abstract class ViewModelBase : ObservableObject
{
    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    /// <summary>Called each time the view model is navigated to.</summary>
    public virtual void OnNavigatedTo() { }

    /// <summary>Called each time the view model is navigated away from.</summary>
    public virtual void OnNavigatedFrom() { }
}

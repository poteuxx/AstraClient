namespace AstraClient.Mvvm;

public interface INavigationService
{
    ViewModelBase? Current { get; }
    event EventHandler? CurrentChanged;
    void NavigateTo<T>() where T : ViewModelBase;
    void NavigateTo(ViewModelBase viewModel);
    T Resolve<T>() where T : ViewModelBase;
}

/// <summary>Swaps the active page view model; resolves pages as singletons from the container.</summary>
public sealed class NavigationService : INavigationService
{
    private readonly ServiceContainer _container;

    public NavigationService(ServiceContainer container) => _container = container;

    public ViewModelBase? Current { get; private set; }

    public event EventHandler? CurrentChanged;

    public T Resolve<T>() where T : ViewModelBase => _container.Get<T>();

    public void NavigateTo<T>() where T : ViewModelBase => NavigateTo(Resolve<T>());

    public void NavigateTo(ViewModelBase viewModel)
    {
        if (ReferenceEquals(Current, viewModel))
        {
            viewModel.OnNavigatedTo();
            return;
        }

        Current?.OnNavigatedFrom();
        Current = viewModel;
        viewModel.OnNavigatedTo();
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }
}

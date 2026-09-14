namespace AstraClient.Mvvm;

/// <summary>A minimal hand-rolled service container (no external DI dependency).</summary>
public sealed class ServiceContainer
{
    private readonly Dictionary<Type, object> _instances = new();
    private readonly Dictionary<Type, Func<object>> _factories = new();

    public void RegisterInstance<T>(T instance) where T : class
        => _instances[typeof(T)] = instance ?? throw new ArgumentNullException(nameof(instance));

    public void RegisterInstance(Type type, object instance)
        => _instances[type] = instance ?? throw new ArgumentNullException(nameof(instance));

    public void RegisterFactory<T>(Func<T> factory) where T : class
        => _factories[typeof(T)] = () => factory();

    public T Get<T>() where T : class
        => (T)Get(typeof(T));

    public object Get(Type type)
    {
        if (_instances.TryGetValue(type, out var existing))
            return existing;

        if (_factories.TryGetValue(type, out var factory))
        {
            var created = factory();
            _instances[type] = created; // singleton by default
            return created;
        }

        throw new InvalidOperationException($"Service not registered: {type.Name}");
    }

    public bool TryGet<T>(out T? service) where T : class
    {
        if (_instances.TryGetValue(typeof(T), out var existing))
        {
            service = (T)existing;
            return true;
        }
        service = null;
        return false;
    }
}

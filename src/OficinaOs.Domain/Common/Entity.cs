namespace OficinaOs.Domain.Common;

public abstract class Entity
{
    private readonly List<IDomainEvent> _events = new();
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _events.AsReadOnly();
    protected void RaiseEvent(IDomainEvent evt) => _events.Add(evt);
    public void ClearEvents() => _events.Clear();
}

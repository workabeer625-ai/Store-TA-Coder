namespace Marina.Domain.Common;
public abstract class Entity { public Guid Id { get; set; } = Guid.NewGuid(); public DateTime CreatedUtc { get; set; } = DateTime.UtcNow; public DateTime? UpdatedUtc { get; set; } public bool IsDeleted { get; set; } }

namespace GymManagement.Domain.Common;

/// <summary>Base class for every persisted entity.</summary>
public abstract class BaseEntity
{
    public int Id { get; set; }
}

/// <summary>Adds creation/modification tracking columns.</summary>
public abstract class AuditableEntity : BaseEntity
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
}

/// <summary>Marks an entity that is never physically deleted by normal operations.</summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
    int? DeletedBy { get; set; }
}

/// <summary>Auditable entity that also supports the recycle bin / soft delete workflow.</summary>
public abstract class SoftDeletableEntity : AuditableEntity, ISoftDeletable
{
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int? DeletedBy { get; set; }
}

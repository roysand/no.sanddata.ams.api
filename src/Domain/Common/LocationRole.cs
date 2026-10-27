namespace Domain.Common;

/// <summary>What a user may do at a location they are linked to: owners manage it, viewers only read it.</summary>
public enum LocationRole
{
    Owner,
    Viewer
}

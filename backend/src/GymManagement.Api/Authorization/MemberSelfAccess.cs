using GymManagement.Application.Common;

namespace GymManagement.Api.Authorization;

/// <summary>
/// The one rule that lets a signed-in member read their own records.
/// </summary>
/// <remarks>
/// <para>
/// Member-scoped read endpoints cannot simply carry <c>[HasPermission]</c>: the Member role does not
/// hold <c>members.view</c>, <c>payments.view</c> and friends, and granting them would open every
/// other member's record too. Instead those endpoints stay on the controller's default
/// <c>[Authorize]</c> and call in here, which admits the caller when either
/// </para>
/// <list type="bullet">
///   <item>they hold the staff permission the endpoint is normally gated on, or</item>
///   <item>the record belongs to the member the caller is signed in as.</item>
/// </list>
/// <para>
/// The owning member id is always read from <see cref="ICurrentUserService.MemberId"/>, which comes
/// from the <c>memberId</c> claim on the validated JWT. It is never taken from a route value, query
/// string, header or request body, so changing the id in the URL cannot widen what a member sees.
/// </para>
/// </remarks>
public static class MemberSelfAccess
{
    /// <summary>
    /// Throws <see cref="ForbiddenAppException"/> unless the caller holds <paramref name="permission"/>
    /// or is signed in as member <paramref name="memberId"/>.
    /// </summary>
    /// <param name="currentUser">The caller, resolved from the validated JWT.</param>
    /// <param name="permission">The permission that normally gates the endpoint.</param>
    /// <param name="memberId">The member the requested record belongs to.</param>
    /// <param name="records">
    /// Names the records in the refusal message, e.g. "payments" gives "You may only view your own payments."
    /// </param>
    public static void EnsureCanRead(
        ICurrentUserService currentUser, string permission, int memberId, string records)
    {
        ArgumentNullException.ThrowIfNull(currentUser);

        // Staff, trainers and admins keep exactly the access they had before.
        if (currentUser.HasPermission(permission)) return;

        // Everyone else is a member reading their own record, or nobody.
        var ownMemberId = currentUser.MemberId;
        if (ownMemberId is null || ownMemberId.Value != memberId)
            throw new ForbiddenAppException($"You may only view your own {records}.");
    }

    /// <summary>
    /// The same rule for a record reached by its own id rather than by member id — a payment receipt,
    /// say — where the owning member had to be looked up first. A <see langword="null"/>
    /// <paramref name="ownerMemberId"/> means the record does not exist or was deleted; that is
    /// refused rather than reported, so a member cannot probe which ids are real.
    /// </summary>
    public static void EnsureCanRead(
        ICurrentUserService currentUser, string permission, int? ownerMemberId, string records)
    {
        ArgumentNullException.ThrowIfNull(currentUser);

        if (currentUser.HasPermission(permission)) return;

        var ownMemberId = currentUser.MemberId;
        if (ownMemberId is null || ownerMemberId is null || ownerMemberId.Value != ownMemberId.Value)
            throw new ForbiddenAppException($"You may only view your own {records}.");
    }
}

using GymManagement.Application.Common;
using GymManagement.Application.DTOs;

namespace GymManagement.Application.Interfaces;

/// <summary>Gym equipment inventory and its service schedule.</summary>
public interface IEquipmentService
{
    Task<PagedResult<EquipmentListDto>> GetPagedAsync(EquipmentQueryDto query, CancellationToken ct = default);
    Task<EquipmentDetailDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<EquipmentDetailDto> SaveAsync(SaveEquipmentDto dto, CancellationToken ct = default);
    Task SoftDeleteAsync(int id, CancellationToken ct = default);
    Task RestoreAsync(int id, CancellationToken ct = default);

    /// <summary>Distinct categories already in use, for the filter drop-down.</summary>
    Task<List<string>> GetCategoriesAsync(CancellationToken ct = default);
}

/// <summary>Walk-in, phone and website leads, up to the point they become members.</summary>
public interface IEnquiryService
{
    Task<PagedResult<EnquiryListDto>> GetPagedAsync(EnquiryQueryDto query, CancellationToken ct = default);
    Task<EnquiryDetailDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<EnquiryDetailDto> SaveAsync(SaveEnquiryDto dto, CancellationToken ct = default);
    Task SoftDeleteAsync(int id, CancellationToken ct = default);
    Task RestoreAsync(int id, CancellationToken ct = default);

    /// <summary>Links an enquiry to the member record it produced and marks it converted.</summary>
    Task<EnquiryDetailDto> ConvertToMemberAsync(int enquiryId, int memberId, CancellationToken ct = default);
}

/// <summary>Member suggestions, complaints and ratings, plus the back-office reply.</summary>
public interface IFeedbackService
{
    Task<PagedResult<FeedbackListDto>> GetPagedAsync(FeedbackQueryDto query, CancellationToken ct = default);
    Task<FeedbackDetailDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<FeedbackDetailDto> SaveAsync(SaveFeedbackDto dto, CancellationToken ct = default);
    Task SoftDeleteAsync(int id, CancellationToken ct = default);
    Task RestoreAsync(int id, CancellationToken ct = default);

    /// <summary>Records the administrator reply and moves the item to Reviewed.</summary>
    Task<FeedbackDetailDto> RespondAsync(int id, string response, CancellationToken ct = default);

    /// <summary>Member-facing submission. The author is taken from the signed-in member.</summary>
    Task<FeedbackDetailDto> SubmitAsync(CreateFeedbackDto dto, CancellationToken ct = default);

    /// <summary>Feedback authored by the signed-in member.</summary>
    Task<PagedResult<FeedbackListDto>> GetMineAsync(PagedRequest query, CancellationToken ct = default);
}

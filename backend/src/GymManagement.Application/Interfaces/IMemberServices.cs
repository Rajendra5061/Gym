using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Domain.Enums;

namespace GymManagement.Application.Interfaces;

public interface IMemberService
{
    Task<PagedResult<MemberListDto>> GetPagedAsync(MemberQueryDto query, CancellationToken ct = default);
    Task<MemberDetailDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<MemberDetailDto?> GetByCodeAsync(string memberCode, CancellationToken ct = default);
    Task<MemberHistoryDto> GetHistoryAsync(int id, CancellationToken ct = default);

    /// <summary>Creates the member and, when requested, a linked login account.</summary>
    Task<(MemberDetailDto Member, TemporaryPasswordDto? Account)> CreateAsync(CreateMemberDto dto, CancellationToken ct = default);
    Task<MemberDetailDto> UpdateAsync(UpdateMemberDto dto, CancellationToken ct = default);
    Task SetStatusAsync(int id, MemberStatus status, string? reason, CancellationToken ct = default);
    Task SoftDeleteAsync(int id, CancellationToken ct = default);
    Task RestoreAsync(int id, CancellationToken ct = default);

    Task<List<LookupDto>> SearchLookupAsync(string? term, int take = 20, CancellationToken ct = default);
    Task AssignTrainerAsync(int memberId, int? trainerId, CancellationToken ct = default);

    Task<List<MemberDocumentDto>> GetDocumentsAsync(int memberId, CancellationToken ct = default);
    Task<MemberDocumentDto> AddDocumentAsync(int memberId, string documentType, string fileName,
        byte[] content, string? contentType, string? notes, CancellationToken ct = default);

    /// <summary>Reads a stored document back so the client can download it.</summary>
    Task<FileExportDto> GetDocumentFileAsync(int documentId, CancellationToken ct = default);

    Task DeleteDocumentAsync(int documentId, CancellationToken ct = default);

    Task<List<MemberMeasurementDto>> GetMeasurementsAsync(int memberId, CancellationToken ct = default);
    Task<MemberMeasurementDto> SaveMeasurementAsync(MemberMeasurementDto dto, CancellationToken ct = default);
    Task DeleteMeasurementAsync(int measurementId, CancellationToken ct = default);
    Task<MemberProgressDto> GetProgressAsync(int memberId, DateTime? from, DateTime? to, CancellationToken ct = default);
}

public interface ITrainerService
{
    Task<PagedResult<TrainerListDto>> GetPagedAsync(TrainerQueryDto query, CancellationToken ct = default);
    Task<TrainerDetailDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<(TrainerDetailDto Trainer, TemporaryPasswordDto? Account)> CreateAsync(CreateTrainerDto dto, CancellationToken ct = default);
    Task<TrainerDetailDto> UpdateAsync(UpdateTrainerDto dto, CancellationToken ct = default);
    Task SetStatusAsync(int id, TrainerStatus status, CancellationToken ct = default);
    Task SoftDeleteAsync(int id, CancellationToken ct = default);
    Task RestoreAsync(int id, CancellationToken ct = default);
    Task<List<LookupDto>> GetLookupAsync(bool onlyActive = true, CancellationToken ct = default);
    Task AssignMembersAsync(AssignTrainerDto dto, CancellationToken ct = default);
    Task<PagedResult<MemberListDto>> GetAssignedMembersAsync(int trainerId, PagedRequest query, CancellationToken ct = default);
    Task<List<TrainerWorkloadDto>> GetWorkloadAsync(DateTime? from, DateTime? to, CancellationToken ct = default);
}

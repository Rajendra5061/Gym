using GymManagement.Api.Authorization;
using GymManagement.Application.Common;
using GymManagement.Application.DTOs;
using GymManagement.Application.Interfaces;
using GymManagement.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GymManagement.Api.Controllers;

/// <summary>Operating expenses and their categories, used by the profit and loss report.</summary>
public sealed class ExpensesController : ApiControllerBase
{
    private readonly IExpenseService _expenses;

    public ExpensesController(IExpenseService expenses) => _expenses = expenses;

    /// <summary>Returns a filtered, paged list of expenses.</summary>
    [HttpGet]
    [HasPermission(Permissions.ExpensesView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ExpenseDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<ExpenseDto>>>> GetPaged(
        [FromQuery] ExpenseQueryDto query, CancellationToken ct) =>
        Success(await _expenses.GetPagedAsync(query, ct));

    /// <summary>Returns a single expense.</summary>
    [HttpGet("{id:int}")]
    [HasPermission(Permissions.ExpensesView)]
    [ProducesResponseType(typeof(ApiResponse<ExpenseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ExpenseDto>>> GetById(int id, CancellationToken ct) =>
        Success(await _expenses.GetByIdAsync(id, ct));

    /// <summary>Records a new expense.</summary>
    [HttpPost]
    [HasPermission(Permissions.ExpensesManage)]
    [ProducesResponseType(typeof(ApiResponse<ExpenseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<ExpenseDto>>> Create(
        [FromBody] SaveExpenseDto dto, CancellationToken ct) =>
        Success(await _expenses.SaveAsync(dto, ct), "Expense recorded.");

    /// <summary>Updates the expense identified by the route id.</summary>
    [HttpPut("{id:int}")]
    [HasPermission(Permissions.ExpensesManage)]
    [ProducesResponseType(typeof(ApiResponse<ExpenseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ExpenseDto>>> Update(
        int id, [FromBody] SaveExpenseDto dto, CancellationToken ct)
    {
        dto.Id = id;
        return Success(await _expenses.SaveAsync(dto, ct), "Expense updated.");
    }

    /// <summary>Soft deletes an expense.</summary>
    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.ExpensesManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Delete(int id, CancellationToken ct)
    {
        await _expenses.SoftDeleteAsync(id, ct);
        return SuccessMessage("Expense deleted.");
    }

    /// <summary>Restores a soft-deleted expense.</summary>
    [HttpPost("{id:int}/restore")]
    [HasPermission(Permissions.ExpensesManage)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Restore(int id, CancellationToken ct)
    {
        await _expenses.RestoreAsync(id, ct);
        return SuccessMessage("Expense restored.");
    }

    /// <summary>Expense categories with their running totals.</summary>
    [HttpGet("categories")]
    [HasPermission(Permissions.ExpensesView)]
    [ProducesResponseType(typeof(ApiResponse<List<ExpenseCategoryDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<ExpenseCategoryDto>>>> GetCategories(CancellationToken ct) =>
        Success(await _expenses.GetCategoriesAsync(ct));

    /// <summary>Creates or updates an expense category.</summary>
    [HttpPost("categories")]
    [HasPermission(Permissions.ExpensesManage)]
    [ProducesResponseType(typeof(ApiResponse<ExpenseCategoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<ExpenseCategoryDto>>> SaveCategory(
        [FromBody] ExpenseCategoryDto dto, CancellationToken ct) =>
        Success(await _expenses.SaveCategoryAsync(dto, ct), "Expense category saved.");
}

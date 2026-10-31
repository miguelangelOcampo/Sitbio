namespace Sitbio.Application.Comun;

/// <summary>Lista paginada (DOC-06 9.3): { items, page, pageSize, total }, con pageSize ≤ 100.</summary>
public sealed record Pagina<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)
{
    public const int TamanoMaximo = 100;
}

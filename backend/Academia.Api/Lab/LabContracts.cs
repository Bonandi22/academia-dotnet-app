using Academia.Infrastructure.Lab;

namespace Academia.Api.Lab;

public sealed record TransacaoDto(long Id, int ContaId, DateTime DataMovimento, decimal Valor, string Tipo, string? Descricao);

public sealed record Cursor(DateTime DepoisData, long DepoisId);

public sealed record PaginaResposta(
    string Estrategia,
    double TempoMs,
    int Quantidade,
    IReadOnlyList<TransacaoDto> Itens,
    Cursor? ProximoCursor,
    string SqlGerado);

public sealed record CriarTransacaoRequest(int ContaId, decimal Valor, string Tipo, string? Descricao);

public sealed record ResultadoBenchmark(string Metodo, int Linhas, double Segundos, double LinhasPorSegundo);

public sealed record EstadoSeed(
    string Situacao,
    long Inseridas,
    long Total,
    DateTime? IniciadoEm,
    DateTime? TerminadoEm,
    SeedResultado? Resultado,
    string? Erro);

// Tipos lidos com Database.SqlQueryRaw<T>: as propriedades têm de ter o mesmo nome das colunas devolvidas.
public sealed class LinhaRelatorioMensal
{
    public int Ano { get; set; }
    public int Mes { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public long Quantidade { get; set; }
    public decimal Total { get; set; }
}

public sealed class LinhaTopConta
{
    public long Posicao { get; set; }
    public string Numero { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public decimal Saldo { get; set; }
    public long NumTransacoes { get; set; }
}

public sealed class LinhaContagemTabela
{
    public string Tabela { get; set; } = string.Empty;
    public long Linhas { get; set; }
}

public sealed class LinhaIndice
{
    public string Indice { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public decimal TamanhoMb { get; set; }
}

namespace Academia.Domain.Lab;

// Modelo do laboratório de volume. Espelha o modelo dos exercícios SQL do guia:
// Clientes(Id, Nome, Pais), Contas(Id, Numero, ClienteId, DataAbertura),
// Transacoes(Id, ContaId, DataMovimento, Valor, Tipo).

public sealed class Cliente
{
    // Id atribuído pelo seeder (não é IDENTITY) para permitir carga em massa com SqlBulkCopy.
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Pais { get; set; } = "PT";
}

public sealed class Conta
{
    public int Id { get; set; }
    public string Numero { get; set; } = string.Empty;
    public int ClienteId { get; set; }
    public DateTime DataAbertura { get; set; }
}

public sealed class Transacao
{
    // BIGINT IDENTITY como chave clustered: estreita, única e crescente.
    // Comparar com o Guid.NewGuid() das entidades da Academia (ver lab/sql/04-guid-vs-identity.sql).
    public long Id { get; set; }
    public int ContaId { get; set; }
    public DateTime DataMovimento { get; set; }

    // Crédito positivo; débito, pagamento e transferência negativos.
    public decimal Valor { get; set; }
    public string Tipo { get; set; } = TiposTransacao.Debito;
    public string? Descricao { get; set; }
}

public static class TiposTransacao
{
    public const string Credito = "CREDITO";
    public const string Debito = "DEBITO";
    public const string Pagamento = "PAGAMENTO";
    public const string Transferencia = "TRANSFERENCIA";

    public static readonly string[] Todos = [Credito, Debito, Pagamento, Transferencia];
}

// Regista cada Idempotency-Key já processada. A chave é a PK, por isso um segundo
// pedido com a mesma chave viola a unicidade e nunca cria uma segunda transação.
public sealed class PedidoIdempotente
{
    public string Chave { get; set; } = string.Empty;
    public long TransacaoId { get; set; }
    public string HashPedido { get; set; } = string.Empty;
    public DateTime CriadoEm { get; set; }
}

// Outbox pattern: o evento é gravado na MESMA transação que o dado de negócio.
// Um worker lê esta tabela e publica no broker (aqui simulado com log; em produção, Kafka).
public sealed class OutboxMessage
{
    public long Id { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime CriadoEm { get; set; }
    public DateTime? ProcessadoEm { get; set; }
}

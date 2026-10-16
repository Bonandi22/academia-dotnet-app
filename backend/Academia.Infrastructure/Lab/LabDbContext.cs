using Academia.Domain.Lab;
using Microsoft.EntityFrameworkCore;

namespace Academia.Infrastructure.Lab;

// Contexto separado, numa base de dados separada (AcademiaLab), para que o laboratório
// possa ser recriado e carregado com milhões de linhas sem tocar nos dados da Academia.
public sealed class LabDbContext(DbContextOptions<LabDbContext> options) : DbContext(options)
{
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Conta> Contas => Set<Conta>();
    public DbSet<Transacao> Transacoes => Set<Transacao>();
    public DbSet<PedidoIdempotente> PedidosIdempotentes => Set<PedidoIdempotente>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Cliente>(b =>
        {
            b.ToTable("Clientes");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Nome).HasMaxLength(120).IsUnicode(true).IsRequired();
            b.Property(x => x.Pais).HasMaxLength(2).IsUnicode(false).IsFixedLength().IsRequired();
        });

        modelBuilder.Entity<Conta>(b =>
        {
            b.ToTable("Contas");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Numero).HasMaxLength(20).IsUnicode(false).IsRequired();
            b.Property(x => x.DataAbertura).HasColumnType("date");
            b.HasOne<Cliente>().WithMany().HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Transacao>(b =>
        {
            b.ToTable("Transacoes");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).UseIdentityColumn();
            b.Property(x => x.DataMovimento).HasColumnType("datetime2(3)");
            b.Property(x => x.Valor).HasPrecision(18, 2);

            // VARCHAR (não NVARCHAR). Atenção: um parâmetro NVARCHAR vindo do .NET contra esta
            // coluna provoca conversão implícita e pode anular o uso do índice (exercício no 01-medicoes.sql).
            b.Property(x => x.Tipo).HasMaxLength(20).IsUnicode(false).IsRequired();
            b.Property(x => x.Descricao).HasMaxLength(200).IsUnicode(true);

            // O EF cria por convenção um índice para a FK (IX_Transacoes_ContaId).
            // Os cenários de índices do laboratório removem TODOS os nonclustered antes de criar os seus,
            // por isso esse índice desaparece no cenário "nenhum".
            b.HasOne<Conta>().WithMany().HasForeignKey(x => x.ContaId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<PedidoIdempotente>(b =>
        {
            b.ToTable("PedidosIdempotentes");
            b.HasKey(x => x.Chave);
            b.Property(x => x.Chave).HasMaxLength(100).IsUnicode(false);
            b.Property(x => x.HashPedido).HasMaxLength(64).IsUnicode(false).IsFixedLength();
            b.Property(x => x.CriadoEm).HasColumnType("datetime2(3)");
        });

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("Outbox");
            b.HasKey(x => x.Id);
            b.Property(x => x.Tipo).HasMaxLength(100).IsUnicode(false).IsRequired();
            b.Property(x => x.Payload).IsRequired();
            b.Property(x => x.CriadoEm).HasColumnType("datetime2(3)");
            b.Property(x => x.ProcessadoEm).HasColumnType("datetime2(3)");

            // Índice filtrado: só as mensagens pendentes. Fica pequeno mesmo com milhões de mensagens processadas.
            b.HasIndex(x => x.Id).HasFilter("[ProcessadoEm] IS NULL").HasDatabaseName("IX_Outbox_Pendentes");
        });
    }
}

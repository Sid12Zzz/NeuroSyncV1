using Microsoft.EntityFrameworkCore;
using NeuroSync.Models;

namespace NeuroSync.Data;

/// <summary>
/// Contexto principal do Entity Framework Core para o sistema NeuroSync.
/// Gerencia as tabelas clínicas, financeiras, de usuários e do prontuário eletrônico no SQLite.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{                                                       //Os dois pontos indicam herança em C#. Aqui, AppDbContext herda de DbContext, que é a classe base do Entity Framework Core para interagir com o banco de dados.]

    // --- Autenticação e Usuários ---
    //Cada DBset referencia uma tabela no banco de dados, assim como uma entidade.
    public DbSet<Usuario> Usuarios { get; set; }

    // --- Pacientes e Prontuário Clínico ---
    public DbSet<Paciente> Pacientes { get; set; }
    public DbSet<Prontuario> Prontuarios { get; set; }
    public DbSet<Evolucao> Evolucoes { get; set; }
    public DbSet<ParecerTecnico> PareceresTecnicos { get; set; }
    public DbSet<Anexo> Anexos { get; set; }

    // --- Agenda e Atendimentos ---
    public DbSet<Agendamento> Agendamentos { get; set; }

    // --- Gestão Financeira (Receitas e Despesas) ---
    public DbSet<Cobranca> Cobrancas { get; set; }
    public DbSet<Despesa> Despesas { get; set; }

    // --- Entidades Legadas mantidas para integridade do banco SQLite ---
    public DbSet<Profissional> Profissionais { get; set; }
    public DbSet<Sessao> Sessoes { get; set; }
    public DbSet<Agenda> Agendas { get; set; }
    public DbSet<Pagamento> Pagamentos { get; set; }

    //Serve para informar qual banco de dados será utilizado ao entity framework.
    //Assim como onde está localizado o banco de dados (Data source=neurosync.db).
    
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Garante a conexão com o banco SQLite local neurosync.db
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite("Data Source=neurosync.db");
        }
    }
}
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OficinaOs.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ordens_servico",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: false),
                    VeiculoId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefeitoRelatado = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CriadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DiagnosticoId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrcamentoId = table.Column<Guid>(type: "uuid", nullable: true),
                    PagamentoId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ordens_servico", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "historico_status_os",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrdemServicoId = table.Column<Guid>(type: "uuid", nullable: false),
                    StatusAnterior = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    StatusNovo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    MudouEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoPor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_historico_status_os", x => x.Id);
                    table.ForeignKey(
                        name: "FK_historico_status_os_ordens_servico_OrdemServicoId",
                        column: x => x.OrdemServicoId,
                        principalTable: "ordens_servico",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ordem_servico_itens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrdemServicoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReferenciaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Descricao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Quantidade = table.Column<int>(type: "integer", nullable: false),
                    PrecoUnitario = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ordem_servico_itens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ordem_servico_itens_ordens_servico_OrdemServicoId",
                        column: x => x.OrdemServicoId,
                        principalTable: "ordens_servico",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_historico_status_os_OrdemServicoId",
                table: "historico_status_os",
                column: "OrdemServicoId");

            migrationBuilder.CreateIndex(
                name: "IX_ordem_servico_itens_OrdemServicoId",
                table: "ordem_servico_itens",
                column: "OrdemServicoId");

            migrationBuilder.CreateIndex(
                name: "IX_ordens_servico_ClienteId",
                table: "ordens_servico",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_ordens_servico_Status",
                table: "ordens_servico",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "historico_status_os");

            migrationBuilder.DropTable(
                name: "ordem_servico_itens");

            migrationBuilder.DropTable(
                name: "ordens_servico");
        }
    }
}

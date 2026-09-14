using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Parorendeportalen.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVedtak : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ServiceType",
                table: "Visits",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "Vedtak",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    CareRecipientId = table.Column<int>(type: "integer", nullable: false),
                    ServiceType = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    Title = table.Column<string>(
                        type: "character varying(300)",
                        maxLength: 300,
                        nullable: false
                    ),
                    RecurrenceDays = table.Column<int>(type: "integer", nullable: false),
                    RecurrenceTimesPerDay = table.Column<int>(type: "integer", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vedtak", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vedtak_CareRecipients_CareRecipientId",
                        column: x => x.CareRecipientId,
                        principalTable: "CareRecipients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "VedtakTasks",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    VedtakId = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: false
                    ),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VedtakTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VedtakTasks_Vedtak_VedtakId",
                        column: x => x.VedtakId,
                        principalTable: "Vedtak",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Vedtak_CareRecipientId_ValidFrom",
                table: "Vedtak",
                columns: new[] { "CareRecipientId", "ValidFrom" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_VedtakTasks_VedtakId_Sequence",
                table: "VedtakTasks",
                columns: new[] { "VedtakId", "Sequence" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "VedtakTasks");

            migrationBuilder.DropTable(name: "Vedtak");

            migrationBuilder.DropColumn(name: "ServiceType", table: "Visits");
        }
    }
}

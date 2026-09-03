using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Parorendeportalen.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConsentAndAccessLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccessLogEntries",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    OccurredAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    NextOfKinId = table.Column<int>(type: "integer", nullable: false),
                    CareRecipientId = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    Outcome = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessLogEntries", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "Consents",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    CareRecipientId = table.Column<int>(type: "integer", nullable: false),
                    NextOfKinId = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    ValidFrom = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    ValidTo = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Consents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Consents_CareRecipients_CareRecipientId",
                        column: x => x.CareRecipientId,
                        principalTable: "CareRecipients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_Consents_NextOfKin_NextOfKinId",
                        column: x => x.NextOfKinId,
                        principalTable: "NextOfKin",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AccessLogEntries_CareRecipientId_OccurredAt",
                table: "AccessLogEntries",
                columns: new[] { "CareRecipientId", "OccurredAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AccessLogEntries_NextOfKinId_OccurredAt",
                table: "AccessLogEntries",
                columns: new[] { "NextOfKinId", "OccurredAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Consents_CareRecipientId_NextOfKinId_Category",
                table: "Consents",
                columns: new[] { "CareRecipientId", "NextOfKinId", "Category" },
                unique: true,
                filter: "\"ValidTo\" IS NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Consents_NextOfKinId",
                table: "Consents",
                column: "NextOfKinId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AccessLogEntries");

            migrationBuilder.DropTable(name: "Consents");
        }
    }
}

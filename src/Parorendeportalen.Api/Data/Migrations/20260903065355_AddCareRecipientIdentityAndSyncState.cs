using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Parorendeportalen.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCareRecipientIdentityAndSyncState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Visits_Origin_ExternalId", table: "Visits");

            migrationBuilder.AddColumn<string>(
                name: "NationalIdHash",
                table: "CareRecipients",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "SyncRuns",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    SourceSystem = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    ResourceType = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    StartedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    CompletedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    Status = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    Inserted = table.Column<int>(type: "integer", nullable: false),
                    Updated = table.Column<int>(type: "integer", nullable: false),
                    Unchanged = table.Column<int>(type: "integer", nullable: false),
                    Unresolved = table.Column<int>(type: "integer", nullable: false),
                    Truncated = table.Column<bool>(type: "boolean", nullable: false),
                    Error = table.Column<string>(
                        type: "character varying(2000)",
                        maxLength: 2000,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncRuns", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "SyncWatermarks",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    SourceSystem = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    ResourceType = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    SourceUpdatedThrough = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    ContinuationToken = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                    UnresolvedFrom = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncWatermarks", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Visits_ExternalId_Origin",
                table: "Visits",
                columns: new[] { "ExternalId", "Origin" },
                unique: true,
                filter: "\"ExternalId\" IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_CareRecipients_NationalIdHash",
                table: "CareRecipients",
                column: "NationalIdHash",
                unique: true,
                filter: "\"NationalIdHash\" IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_SyncRuns_SourceSystem_ResourceType_StartedAt",
                table: "SyncRuns",
                columns: new[] { "SourceSystem", "ResourceType", "StartedAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SyncWatermarks_SourceSystem_ResourceType",
                table: "SyncWatermarks",
                columns: new[] { "SourceSystem", "ResourceType" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SyncRuns");

            migrationBuilder.DropTable(name: "SyncWatermarks");

            migrationBuilder.DropIndex(name: "IX_Visits_ExternalId_Origin", table: "Visits");

            migrationBuilder.DropIndex(
                name: "IX_CareRecipients_NationalIdHash",
                table: "CareRecipients"
            );

            migrationBuilder.DropColumn(name: "NationalIdHash", table: "CareRecipients");

            migrationBuilder.CreateIndex(
                name: "IX_Visits_Origin_ExternalId",
                table: "Visits",
                columns: new[] { "Origin", "ExternalId" },
                unique: true,
                filter: "\"ExternalId\" IS NOT NULL"
            );
        }
    }
}

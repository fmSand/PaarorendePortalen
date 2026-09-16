using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Parorendeportalen.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVisitWritePath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Visits",
                type: "timestamp with time zone",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "CreatedByNextOfKinId",
                table: "Visits",
                type: "integer",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Visits",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "Visits",
                type: "timestamp with time zone",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Visibility",
                table: "Visits",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true
            );

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Visits",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u
            );

            // Every row logged before this migration was a read.
            migrationBuilder.AddColumn<string>(
                name: "Operation",
                table: "AccessLogEntries",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Read"
            );

            migrationBuilder.CreateTable(
                name: "VisitComments",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    VisitId = table.Column<int>(type: "integer", nullable: false),
                    AuthorNextOfKinId = table.Column<int>(type: "integer", nullable: false),
                    Body = table.Column<string>(
                        type: "character varying(2000)",
                        maxLength: 2000,
                        nullable: false
                    ),
                    Visibility = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    CreatedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdatedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisitComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VisitComments_NextOfKin_AuthorNextOfKinId",
                        column: x => x.AuthorNextOfKinId,
                        principalTable: "NextOfKin",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_VisitComments_Visits_VisitId",
                        column: x => x.VisitId,
                        principalTable: "Visits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Visits_CreatedByNextOfKinId",
                table: "Visits",
                column: "CreatedByNextOfKinId"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_Visits_AuthoredEntryHasVisibility",
                table: "Visits",
                sql: "(\"CreatedByNextOfKinId\" IS NULL) = (\"Visibility\" IS NULL)"
            );

            migrationBuilder.CreateIndex(
                name: "IX_VisitComments_AuthorNextOfKinId",
                table: "VisitComments",
                column: "AuthorNextOfKinId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_VisitComments_VisitId_CreatedAt",
                table: "VisitComments",
                columns: new[] { "VisitId", "CreatedAt" }
            );

            migrationBuilder.AddForeignKey(
                name: "FK_Visits_NextOfKin_CreatedByNextOfKinId",
                table: "Visits",
                column: "CreatedByNextOfKinId",
                principalTable: "NextOfKin",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Visits_NextOfKin_CreatedByNextOfKinId",
                table: "Visits"
            );

            migrationBuilder.DropTable(name: "VisitComments");

            migrationBuilder.DropIndex(name: "IX_Visits_CreatedByNextOfKinId", table: "Visits");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Visits_AuthoredEntryHasVisibility",
                table: "Visits"
            );

            migrationBuilder.DropColumn(name: "CreatedAt", table: "Visits");

            migrationBuilder.DropColumn(name: "CreatedByNextOfKinId", table: "Visits");

            migrationBuilder.DropColumn(name: "Title", table: "Visits");

            migrationBuilder.DropColumn(name: "UpdatedAt", table: "Visits");

            migrationBuilder.DropColumn(name: "Visibility", table: "Visits");

            migrationBuilder.DropColumn(name: "xmin", table: "Visits");

            migrationBuilder.DropColumn(name: "Operation", table: "AccessLogEntries");
        }
    }
}

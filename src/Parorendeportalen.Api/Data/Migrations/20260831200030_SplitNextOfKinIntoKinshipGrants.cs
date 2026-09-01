using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Parorendeportalen.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SplitNextOfKinIntoKinshipGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Reordered from the scaffolded version, which dropped the old columns
            // before creating the table and so discarded every existing grant.
            migrationBuilder.CreateTable(
                name: "KinshipGrants",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    NextOfKinId = table.Column<int>(type: "integer", nullable: false),
                    CareRecipientId = table.Column<int>(type: "integer", nullable: false),
                    Relationship = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: true
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
                    table.PrimaryKey("PK_KinshipGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KinshipGrants_CareRecipients_CareRecipientId",
                        column: x => x.CareRecipientId,
                        principalTable: "CareRecipients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_KinshipGrants_NextOfKin_NextOfKinId",
                        column: x => x.NextOfKinId,
                        principalTable: "NextOfKin",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_KinshipGrants_CareRecipientId",
                table: "KinshipGrants",
                column: "CareRecipientId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_KinshipGrants_NextOfKinId_CareRecipientId",
                table: "KinshipGrants",
                columns: new[] { "NextOfKinId", "CareRecipientId" },
                unique: true
            );

            migrationBuilder.Sql(
                """
                INSERT INTO "KinshipGrants" ("NextOfKinId", "CareRecipientId", "Relationship", "ValidFrom", "ValidTo")
                SELECT "Id", "CareRecipientId", "Relationship", "ValidFrom", "ValidTo"
                FROM "NextOfKin";
                """
            );

            migrationBuilder.DropForeignKey(
                name: "FK_NextOfKin_CareRecipients_CareRecipientId",
                table: "NextOfKin"
            );

            migrationBuilder.DropIndex(name: "IX_NextOfKin_CareRecipientId", table: "NextOfKin");

            migrationBuilder.DropColumn(name: "CareRecipientId", table: "NextOfKin");

            migrationBuilder.DropColumn(name: "Relationship", table: "NextOfKin");

            migrationBuilder.DropColumn(name: "ValidFrom", table: "NextOfKin");

            migrationBuilder.DropColumn(name: "ValidTo", table: "NextOfKin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CareRecipientId",
                table: "NextOfKin",
                type: "integer",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AddColumn<string>(
                name: "Relationship",
                table: "NextOfKin",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true
            );

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ValidFrom",
                table: "NextOfKin",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(
                    new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                    new TimeSpan(0, 0, 0, 0, 0)
                )
            );

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ValidTo",
                table: "NextOfKin",
                type: "timestamp with time zone",
                nullable: true
            );

            // The old schema held one recipient per person, so collapse to the
            // earliest grant. Rows left without one can't be represented, and are
            // deleted rather than pointed at a non-existent recipient.
            migrationBuilder.Sql(
                """
                UPDATE "NextOfKin" n
                SET "CareRecipientId" = g."CareRecipientId",
                    "Relationship"    = g."Relationship",
                    "ValidFrom"       = g."ValidFrom",
                    "ValidTo"         = g."ValidTo"
                FROM (
                    SELECT DISTINCT ON ("NextOfKinId")
                           "NextOfKinId", "CareRecipientId", "Relationship", "ValidFrom", "ValidTo"
                    FROM "KinshipGrants"
                    ORDER BY "NextOfKinId", "ValidFrom", "Id"
                ) g
                WHERE g."NextOfKinId" = n."Id";
                """
            );

            migrationBuilder.Sql("""DELETE FROM "NextOfKin" WHERE "CareRecipientId" = 0;""");

            migrationBuilder.DropTable(name: "KinshipGrants");

            migrationBuilder.CreateIndex(
                name: "IX_NextOfKin_CareRecipientId",
                table: "NextOfKin",
                column: "CareRecipientId"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_NextOfKin_CareRecipients_CareRecipientId",
                table: "NextOfKin",
                column: "CareRecipientId",
                principalTable: "CareRecipients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade
            );
        }
    }
}

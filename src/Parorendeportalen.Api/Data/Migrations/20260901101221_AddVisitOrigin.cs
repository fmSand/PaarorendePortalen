using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parorendeportalen.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVisitOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Visits",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            // Hand-corrected: EF generates defaultValue "", which is not a valid
            // Origin and would throw on read. Portal is the conservative default.
            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "Visits",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Portal");

            migrationBuilder.CreateIndex(
                name: "IX_Visits_Origin_ExternalId",
                table: "Visits",
                columns: new[] { "Origin", "ExternalId" },
                unique: true,
                filter: "\"ExternalId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Visits_Origin_ExternalId",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "Visits");
        }
    }
}

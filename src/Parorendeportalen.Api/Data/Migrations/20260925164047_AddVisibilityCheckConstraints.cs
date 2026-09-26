using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parorendeportalen.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVisibilityCheckConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_Visits_Visibility",
                table: "Visits",
                sql: "\"Visibility\" IN ('Private', 'Shared')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_VisitComments_Visibility",
                table: "VisitComments",
                sql: "\"Visibility\" IN ('Private', 'Shared')"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(name: "CK_Visits_Visibility", table: "Visits");

            migrationBuilder.DropCheckConstraint(
                name: "CK_VisitComments_Visibility",
                table: "VisitComments"
            );
        }
    }
}

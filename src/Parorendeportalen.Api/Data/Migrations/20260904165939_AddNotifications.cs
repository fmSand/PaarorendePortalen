using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Parorendeportalen.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChangeEvents",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    CareRecipientId = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    Kind = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    VisitId = table.Column<int>(type: "integer", nullable: true),
                    ScheduledAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    OccurredAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    ProcessedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChangeEvents_CareRecipients_CareRecipientId",
                        column: x => x.CareRecipientId,
                        principalTable: "CareRecipients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_ChangeEvents_Visits_VisitId",
                        column: x => x.VisitId,
                        principalTable: "Visits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "integer", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    NextOfKinId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationPreferences_NextOfKin_NextOfKinId",
                        column: x => x.NextOfKinId,
                        principalTable: "NextOfKin",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation(
                            "Npgsql:ValueGenerationStrategy",
                            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                        ),
                    NextOfKinId = table.Column<int>(type: "integer", nullable: false),
                    CareRecipientId = table.Column<int>(type: "integer", nullable: false),
                    ChangeEventId = table.Column<long>(type: "bigint", nullable: false),
                    Category = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    Kind = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    VisitId = table.Column<int>(type: "integer", nullable: true),
                    ScheduledAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    OccurredAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    ReadAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_CareRecipients_CareRecipientId",
                        column: x => x.CareRecipientId,
                        principalTable: "CareRecipients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_Notifications_NextOfKin_NextOfKinId",
                        column: x => x.NextOfKinId,
                        principalTable: "NextOfKin",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChangeEvents_CareRecipientId",
                table: "ChangeEvents",
                column: "CareRecipientId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChangeEvents_ProcessedAt",
                table: "ChangeEvents",
                column: "ProcessedAt"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChangeEvents_VisitId",
                table: "ChangeEvents",
                column: "VisitId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_NextOfKinId_Kind",
                table: "NotificationPreferences",
                columns: new[] { "NextOfKinId", "Kind" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_CareRecipientId",
                table: "Notifications",
                column: "CareRecipientId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ChangeEventId_NextOfKinId",
                table: "Notifications",
                columns: new[] { "ChangeEventId", "NextOfKinId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_NextOfKinId_OccurredAt",
                table: "Notifications",
                columns: new[] { "NextOfKinId", "OccurredAt" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ChangeEvents");

            migrationBuilder.DropTable(name: "NotificationPreferences");

            migrationBuilder.DropTable(name: "Notifications");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InSalute.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Dispatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Dispatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    PatientFirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PatientLastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RecipientEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    AttachmentsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InvoiceFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    ProtectionKind = table.Column<int>(type: "int", nullable: false),
                    ReportStatus = table.Column<int>(type: "int", nullable: false),
                    ReportSentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReportError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PasswordStatus = table.Column<int>(type: "int", nullable: false),
                    PasswordSentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PasswordError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsLegacyImport = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Dispatches_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Dispatches_CreatedAt",
                table: "Dispatches",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Dispatches_CreatedByUserId",
                table: "Dispatches",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Dispatches_RecipientEmail",
                table: "Dispatches",
                column: "RecipientEmail");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Dispatches");
        }
    }
}

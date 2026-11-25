using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddDirectoryGroupSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DirectoryGroupEmail",
                table: "Groups",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DirectoryOutboxTasks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TaskType = table.Column<int>(type: "integer", nullable: false),
                    GroupEmail = table.Column<string>(type: "text", nullable: false),
                    MemberEmail = table.Column<string>(type: "text", nullable: true),
                    OldMemberEmail = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirectoryOutboxTasks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Groups_DirectoryGroupEmail",
                table: "Groups",
                column: "DirectoryGroupEmail",
                unique: true,
                filter: "\"DirectoryGroupEmail\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DirectoryOutboxTasks");

            migrationBuilder.DropIndex(
                name: "IX_Groups_DirectoryGroupEmail",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "DirectoryGroupEmail",
                table: "Groups");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberDirectoryEmailOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DirectoryEmailOverride",
                table: "Members",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Members_DirectoryEmailOverride",
                table: "Members",
                column: "DirectoryEmailOverride",
                unique: true,
                filter: "\"DirectoryEmailOverride\" IS NOT NULL AND \"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Members_DirectoryEmailOverride",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "DirectoryEmailOverride",
                table: "Members");
        }
    }
}

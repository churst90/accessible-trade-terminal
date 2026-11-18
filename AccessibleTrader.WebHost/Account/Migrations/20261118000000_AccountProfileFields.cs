using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessibleTrader.WebHost.Account.Migrations
{
    /// <inheritdoc />
    public partial class AccountProfileFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "AspNetUsers",
                type: "TEXT",
                maxLength: 30,
                nullable: true,
                collation: "NOCASE");

            migrationBuilder.AddColumn<bool>(
                name: "LeaderboardOptIn",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Partial + NOCASE: any number of accounts may have no display name, and
            // "Cody" and "cody" are the same name. The collation comes from the column
            // added above, so the index inherits it.
            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_DisplayName",
                table: "AspNetUsers",
                column: "DisplayName",
                unique: true,
                filter: "\"DisplayName\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_DisplayName",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LeaderboardOptIn",
                table: "AspNetUsers");
        }
    }
}

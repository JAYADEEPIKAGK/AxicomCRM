using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcxiomCRM.Migrations
{
    /// <inheritdoc />
    public partial class TrackOpportunityClose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAtUtc",
                table: "Opportunities",
                type: "datetime2",
                nullable: true);

            migrationBuilder.Sql("UPDATE [Opportunities] SET [ClosedAtUtc] = [UpdatedAtUtc] WHERE [Stage] = N'Won'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClosedAtUtc",
                table: "Opportunities");
        }
    }
}

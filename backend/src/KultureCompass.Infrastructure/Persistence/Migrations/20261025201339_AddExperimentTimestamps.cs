using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KultureCompass.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExperimentTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "concluded_at",
                table: "marketing_experiments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "started_at",
                table: "marketing_experiments",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "concluded_at",
                table: "marketing_experiments");

            migrationBuilder.DropColumn(
                name: "started_at",
                table: "marketing_experiments");
        }
    }
}

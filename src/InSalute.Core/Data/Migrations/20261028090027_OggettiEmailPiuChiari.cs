using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InSalute.Core.Data.Migrations
{
    /// <summary>
    /// Oggetti delle email più riconoscibili. Si aggiornano solo i modelli che hanno ancora l'oggetto predefinito:
    /// quelli già personalizzati restano come sono.
    /// </summary>
    public partial class OggettiEmailPiuChiari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE EmailTemplates SET Subject = N'Il suo referto – Poliambulatorio InSalute' WHERE Id = 1 AND Subject = N'Referto';");
            migrationBuilder.Sql(
                "UPDATE EmailTemplates SET Subject = N'Password per aprire il suo referto – Poliambulatorio InSalute' WHERE Id = 2 AND Subject = N'Credenziali';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE EmailTemplates SET Subject = N'Referto' WHERE Id = 1 AND Subject = N'Il suo referto – Poliambulatorio InSalute';");
            migrationBuilder.Sql(
                "UPDATE EmailTemplates SET Subject = N'Credenziali' WHERE Id = 2 AND Subject = N'Password per aprire il suo referto – Poliambulatorio InSalute';");
        }
    }
}

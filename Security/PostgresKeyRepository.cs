using System.Xml;
using System.Xml.Linq;
using Dapper;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Npgsql;

namespace Pillar.Security;

// ASP.NET encrypts key material before passing XML to this repository.
// Sharing these records lets independent container instances read the same cookies.
public sealed class PostgresKeyRepository(NpgsqlDataSource source) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        try
        {
            using var connection = source.OpenConnection();
            return connection.Query<string>("SELECT xml FROM pillar_security.data_protection_keys ORDER BY created_at,id")
                .Select(Parse).ToArray();
        }
        catch (PostgresException e) when (e.SqlState is "42P01" or "3F000" or "42501")
        { throw new InvalidOperationException("Data-protection storage is unavailable. Run database/004_data_protection.sql in Neon as the database owner and grant pillar_runtime to the app login.", e); }
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var connection = source.OpenConnection();
        connection.Execute("""
            INSERT INTO pillar_security.data_protection_keys(friendly_name,xml)
            VALUES(@name,@xml)
            """, new { name = friendlyName, xml = element.ToString(SaveOptions.DisableFormatting) });
    }

    private static XElement Parse(string xml)
    {
        using var text = new StringReader(xml);
        using var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XElement.Load(reader);
    }
}

using System;
using System.Globalization;

namespace E3G.BimTools.RevitBC3.Services;

/// <summary>
/// Calcula el IfcGUID de un elemento Revit a partir de su UniqueId,
/// sin necesidad de que el modelo haya sido exportado previamente a IFC.
///
/// ALGORITMO (el mismo que usa Revit internamente al exportar a IFC):
///
///   El UniqueId de Revit tiene el formato:
///     AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE-FFFFFFFF
///   donde AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE es el GUID del episodio
///   del documento y FFFFFFFF es el ElementId en hexadecimal.
///
///   Paso 1 — XOR:
///     Se hacen XOR byte a byte entre los primeros 4 bytes del GUID del
///     episodio (Data1, almacenado en little-endian por .NET) y los 4
///     bytes del ElementId en little-endian.
///
///   Paso 2 — Codificacion IfcGloballyUniqueId (BuildingSMART):
///     El GUID resultante se trata como un entero de 128 bits big-endian
///     (usando su representacion hexadecimal canonica) y se codifica
///     extrayendo 22 grupos de 6 bits desde el bit menos significativo,
///     usando el alfabeto IFC de 64 caracteres.
///     22 * 6 = 132 bits; los 4 bits de cabecera sobrantes son siempre 0.
///
/// El resultado coincide con el IfcGUID que Revit asignaria al exportar
/// ese elemento a IFC.
///
/// Referencias:
///   - BuildingSMART IfcGloballyUniqueId spec (IFC4 ADD2)
///   - IfcOpenShell Python guid.py (implementacion de referencia)
///   - https://thebuildingcoder.typepad.com/blog/2010/06/element-uniqueid-and-ifcguid.html
/// </summary>
public static class RevitIfcGuidCalculator
{
    /// <summary>
    /// Alfabeto de 64 caracteres definido por BuildingSMART para la
    /// representacion compacta de IfcGloballyUniqueId.
    /// </summary>
    private const string Alphabet =
        "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

    /// <summary>
    /// Calcula el IfcGUID a partir del UniqueId de un elemento Revit.
    /// </summary>
    /// <param name="uniqueId">
    /// UniqueId del elemento en formato:
    /// "XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX-YYYYYYYY"
    /// Los cinco primeros segmentos forman el GUID del episodio; el
    /// sexto (YYYYYYYY) es el ElementId en hexadecimal.
    /// </param>
    /// <returns>
    /// IfcGUID de 22 caracteres, o <see langword="null"/> si el UniqueId
    /// no tiene el formato esperado.
    /// </returns>
    public static string? FromUniqueId(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return null;

        try
        {
            // Separar GUID de episodio (5 segmentos) del ElementId (ultimo).
            var lastDash = uniqueId.LastIndexOf('-');
            if (lastDash < 0) return null;

            var episodePart  = uniqueId.Substring(0, lastDash);
            var elementIdHex = uniqueId.Substring(lastDash + 1);

            if (!Guid.TryParse(episodePart, out var episodeGuid))
                return null;

            if (!uint.TryParse(elementIdHex,
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var elementId))
                return null;

            // Paso 1 — XOR: modificar Data1 (bytes [0..3] en LE).
            var bytes = episodeGuid.ToByteArray();
            bytes[0] ^= (byte)( elementId        & 0xFF);
            bytes[1] ^= (byte)((elementId >>  8) & 0xFF);
            bytes[2] ^= (byte)((elementId >> 16) & 0xFF);
            bytes[3] ^= (byte)((elementId >> 24) & 0xFF);

            // Paso 2 — Codificar en IfcGloballyUniqueId (22 chars).
            return EncodeIfcBase64(new Guid(bytes));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Codifica un <see cref="Guid"/> en la representacion compacta
    /// IfcGloballyUniqueId de 22 caracteres.
    ///
    /// El GUID se interpreta como un entero de 128 bits big-endian (su
    /// representacion hexadecimal canonica, que es la que devuelve
    /// <c>ToString("N")</c> en .NET) y se extraen 22 grupos de 6 bits
    /// del LSB al MSB, construyendo la cadena de derecha a izquierda.
    ///
    /// NOTA sobre endianness: <c>Guid.ToString("N")</c> devuelve la
    /// representacion de cadena del GUID en big-endian independientemente
    /// del orden de almacenamiento interno de .NET, por lo que es
    /// equivalente a la interpretacion numerica estandar del GUID tal
    /// como la usa IfcOpenShell (Python: <c>int(g.replace('-',''), 16)</c>).
    /// </summary>
    private static string EncodeIfcBase64(Guid guid)
    {
        // Representacion hexadecimal canonica (32 chars, sin guiones).
        var hex = guid.ToString("N");

        // Dividir en dos uint64 para operar sobre 128 bits sin BigInteger.
        var high = Convert.ToUInt64(hex.Substring(0, 16), 16);
        var low  = Convert.ToUInt64(hex.Substring(16, 16), 16);

        // Extraer 22 grupos de 6 bits desde el LSB, construir de derecha
        // a izquierda (resultado[21] = bits [5..0], resultado[0] = bits
        // [131..126], que son 0 porque el GUID maximo es < 2^128).
        var result = new char[22];
        for (int i = 21; i >= 0; i--)
        {
            result[i] = Alphabet[(int)(low & 63)];

            // Desplazar el numero de 128 bits 6 posiciones a la derecha.
            // Los 6 bits bajos de 'high' pasan a ser los 6 bits altos de
            // 'low' tras el desplazamiento.
            low  = (low >> 6) | (high << 58);
            high >>= 6;
        }

        return new string(result);
    }
}

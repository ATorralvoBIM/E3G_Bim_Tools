namespace E3G.BimTools.DiTools.Core.Models.Parameters;

/// <summary>
/// Tipos de dato disponibles a la hora de CREAR un parametro nuevo desde la UI
/// de ParaManager.  Se mapean a ForgeTypeId/SpecTypeId al crear el parametro.
/// Lista corta y pragmatica: cubre el 95% de los casos reales en BIM.
/// </summary>
public enum ParameterDataTypeOption
{
    Text,
    Integer,
    Number,
    Length,
    Area,
    Volume,
    Angle,
    YesNo,
    Material,
    Url,
    MultilineText,
}

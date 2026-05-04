// ============================================================================
//  PlantillaPorDefecto.cs
//  Plantilla precargada (vacía, sin catálogos) + reglas-ejemplo para Saneamiento.
// ============================================================================

using System;
using System.Collections.Generic;
using E3G.Saneamiento.Presupuesto.Modelo;

namespace E3G.Saneamiento.Presupuesto.Storage
{
    public static class PlantillaPorDefecto
    {
        public static Plantilla Crear() => new Plantilla
        {
            Id = Guid.NewGuid(),
            Nombre = "Per defecte",
            Reglas = new List<Regla>(),
            Catalogos = new List<CatalogoPartidas>(),
            CatalogoPrimarioId = Guid.Empty,
            Version = "1.0",
            FechaModificacion = DateTime.Now
        };

        /// <summary>
        /// Reglas sugeridas para Saneamiento (Apéndice A del diseño).
        /// Apunta a códigos típicos de BEDEC. PartidaCatalogoId queda Guid.Empty
        /// porque el usuario aún no ha decidido de qué catálogo cargado tirarán
        /// — la primera vez que se evalúe, el motor las resolverá contra el
        /// catálogo primario; después el usuario puede editarlas y fijar el
        /// catálogo concreto.
        /// </summary>
        public static IReadOnlyList<Regla> ReglasSugeridasSaneamiento() => new List<Regla>
        {
            // Sanejament — Fecals (un diàmetre = una partida BEDEC concreta)
            new Regla
            {
                Id = Guid.NewGuid(),
                Nombre = "PVC fecals Ø75",
                Categoria = CategoriaSistema.SaneamientoFecales,
                SubFiltro = new SubFiltroDiametroExacto { DiametroMm = 75 },
                Criterio = new CriterioLongitudTotal(),
                PartidaCodigo = "ED15B870",
                CapituloDestino = "02SAN",
                Activa = true,
                Notas = "Bajante PVC SN-4 Ø75"
            },
            new Regla
            {
                Id = Guid.NewGuid(),
                Nombre = "PVC fecals Ø110",
                Categoria = CategoriaSistema.SaneamientoFecales,
                SubFiltro = new SubFiltroDiametroExacto { DiametroMm = 110 },
                Criterio = new CriterioLongitudTotal(),
                PartidaCodigo = "ED15B871",
                CapituloDestino = "02SAN",
                Activa = true,
                Notas = "Bajante PVC SN-4 Ø110"
            },
            new Regla
            {
                Id = Guid.NewGuid(),
                Nombre = "PVC fecals Ø125",
                Categoria = CategoriaSistema.SaneamientoFecales,
                SubFiltro = new SubFiltroDiametroExacto { DiametroMm = 125 },
                Criterio = new CriterioLongitudTotal(),
                PartidaCodigo = "ED15B873",
                CapituloDestino = "02SAN",
                Activa = true,
                Notas = "Bajante PVC SN-4 Ø125"
            },

            // Sanejament — Pluvials
            new Regla
            {
                Id = Guid.NewGuid(),
                Nombre = "Baixants pluvials Ø110",
                Categoria = CategoriaSistema.SaneamientoPluviales,
                SubFiltro = new SubFiltroDiametroExacto { DiametroMm = 110 },
                Criterio = new CriterioLongitudTotal(),
                PartidaCodigo = "ED15B881",
                CapituloDestino = "02SAN",
                Activa = true,
                Notas = "Bajante PVC pluvial Ø110"
            },
            new Regla
            {
                Id = Guid.NewGuid(),
                Nombre = "Baixants pluvials Ø125",
                Categoria = CategoriaSistema.SaneamientoPluviales,
                SubFiltro = new SubFiltroDiametroExacto { DiametroMm = 125 },
                Criterio = new CriterioLongitudTotal(),
                PartidaCodigo = "ED15B883",
                CapituloDestino = "02SAN",
                Activa = true,
                Notas = "Bajante PVC pluvial Ø125"
            },

            // Sanejament — Grises
            new Regla
            {
                Id = Guid.NewGuid(),
                Nombre = "Grises tot diàmetre",
                Categoria = CategoriaSistema.SaneamientoGrises,
                SubFiltro = new SubFiltroNinguno(),
                Criterio = new CriterioLongitudTotal(),
                PartidaCodigo = "ED15B871",
                CapituloDestino = "02SAN",
                Activa = true,
                Notas = "Igual que fecals — mismo tubo"
            }
        };
    }
}

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

// ============================================================================
//  Modelos de datos tipados que reflejan edificio_para_unity.json (P1A4).
//  La trazabilidad es directa: elementTag OpenSees == elementTag de estos
//  objetos == id del GameObject de Unity.
// ============================================================================
namespace EdificioUnity
{
    [Serializable]
    public class ResCombo
    {
        public double N, Vy, Vz, T, My, Mz;
    }

    [Serializable]
    public class EjesLocales
    {
        public double[] x, y, z;
    }

    [Serializable]
    public class ElementoDatos
    {
        public int elementTag;
        public string type;            // columna / muro / viga
        public int iNode, jNode;
        public int sectionTag, matTag, nivel;
        public string descripcion;
        public double[] a, b;          // nodos extremos en metros
        public EjesLocales ejes_locales;
        public bool apoyado_base;
        public string[] restricciones;
        public double? area_trib_m2;
        public Dictionary<string, ResCombo> resultados;  // por combinacion U1..U4

        // el JSON esta en modelo (x,y,z) con z vertical; Unity usa (x, z, y).
        public Vector3 A => ToV3(a);
        public Vector3 B => ToV3(b);
        public Vector3 AX => ToV3(ejes_locales.x);
        public Vector3 AY => ToV3(ejes_locales.y);
        public Vector3 AZ => ToV3(ejes_locales.z);
        internal static Vector3 ToV3(double[] v) =>
            new Vector3((float)(v != null && v.Length > 0 ? v[0] : 0),
                        (float)(v != null && v.Length > 2 ? v[2] : (v != null && v.Length > 1 ? v[1] : 0)),
                        (float)(v != null && v.Length > 1 ? v[1] : 0));
    }

    [Serializable]
    public class NodoDato
    {
        public int nodeTag;
        public double x, y, z;
        public string tipo;            // base / slave
        public int[] restrained;       // 1 = fijo
        public bool es_maestro_diafragma;
    }

    [Serializable]
    public class SeccionDato
    {
        public int tag;
        public string nombre, tipo;
        public int matTag;
        public double b_m, h_m, A_m2, Iy_m4, Iz_m4, J_m4;
        public string plano_fuente;
    }

    [Serializable]
    public class MaterialDato
    {
        public string nombre;
        public double fc_MPa, fy_MPa, E_kPa, G_kPa, Es_kPa;
    }

    [Serializable]
    public class ApoyoDato
    {
        public int nodeTag;
        public double x, y, z;
        public Dictionary<string, double> reacciones;
        public string descripcion;
        public Vector3 Pos => new Vector3((float)x, (float)z, (float)y);
    }

    [Serializable]
    public class AreaTributaria
    {
        public int elementTag;
        public string tipo, dir;
        public int nivel;
        public double area_trib_m2, wG_kN_m, wQ_kN_m;
        public double[][] poligono;
    }

    [Serializable]
    public class Sismo
    {
        public double C_adoptado, V0_kN;
        public double[] f_por_nivel_kN;
    }

    [Serializable]
    public class CargasDato
    {
        public double G_kN_m2, Q_kN_m2, psi_carga_viva, qG_nominal_kN_m2, a_piso_m2;
        public Sismo sismo;
    }

    [Serializable]
    public class PuntoPm
    {
        public double M_kNm, P_kN;
    }

    [Serializable]
    public class ResDC
    {
        public double P_kN, My_kNm, Mz_kNm, M_efectivo_kNm;
        public double M_nom_capacidad_kNm, DCR_M, P_Pn0;
    }

    [Serializable]
    public class RaizEdificio
    {
        public JObject _meta;
        public Dictionary<string, MaterialDato> materiales;
        public Dictionary<string, SeccionDato> secciones;
        public List<NodoDato> nodos;
        public List<JObject> losas;                       // geometria: se dibuja
        public List<ElementoDatos> elementos;             // 390 elementos
        public List<ApoyoDato> apoyos;
        public List<AreaTributaria> areas_tributarias;
        public CargasDato cargas;
        public Dictionary<string, Dictionary<string, double[]>> deformada;
        public Dictionary<string, JObject> demanda_capacidad;
    }
}
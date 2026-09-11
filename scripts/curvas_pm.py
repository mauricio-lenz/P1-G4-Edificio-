"""
P1A3 - Envolventes P-M de COLUMNA y MURO + verificacion RC + primera
       demanda-capacidad.

Usa el analizador de secciones 'momento_curvatura' (fibras, compatibilidad
de deformaciones) para generar la envolvente P-M (P compresion +), y luego:

  1) Verificacion RC de la seccion (ACI 318 / CIRSOC):
       Pn0  = 0.85 f'c (Ag - Ast) + fy Ast        (compresion pura, 22.4.2.2)
       punto balanceado (cb, Pb, Mb)
       flexa pura  (P = 0 -> Mb)
       traccion pura T = -fy Ast
  2) Demanda-capacidad: se leen las demandas reales (P, My, Mz) de
       reports/salidas/combinaciones.json (superposicion U1-U4) para
       la columna mas cargada (elem. 11020, eje F-2, nivel 1) y el muro
       del nucleo mas demandado (elem. 11033, X=4250 Y=3576, nivel 1),
       y se ubican sobre las respectivas curvas P-M. Momento efectivo
       biaxial: M_t = sqrt(My^2 + Mz^2).

Salidas en reports/salidas/curvas_pm.json
Figuras en reports/fig/pm_columna.png y pm_muro.png

NOTA DE CRITERIO (caso de conductor de los resultados):
  - Para la COLUMNA, la armadura asumida es 12phi25 (rho=1.20%).
  - Para el MURO se usa la seccion unitaria del modelo (MUR_20: e=0.20 m,
    longitud equivalente L=1.0 m, 2 capas phi12@200). No es la longitud
    real del muro del nucleo, por lo que su capacidad es --por unidad de
    longitud-- y la DCR puede indicar exceso de demanda: es un resultado
    esperable que debe revisarse definiendo la longitud real del muro.
"""
import json
import os
import sys

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

from momento_curvatura import (FC, FY, EPS0, EPSCU, EY, ES_SU,
                               COL70, MUR20, punto_balance, puntos_pm)

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SALIDAS = os.path.join(RAIZ, "reports", "salidas")
FIG = os.path.join(RAIZ, "reports", "fig")


def curva_pm(sec, n_strips=64):
    """Envolvente P-M: lista de (P_kN, M_kNm) desde puntos en N y N·mm."""
    pts, _ = puntos_pm(sec, n_strips=n_strips)
    env = [(P / 1e3, M / 1e6) for P, M, c, lbl in pts if M > 0]
    return env


def capacidad_a_P(env, P_d, tol_frac=0.015):
    """Capacidad M en la envolvente para la axial demandada P_d (kN).

    Toma el maximo M entre los puntos con axial cercana a P_d.
    """
    pmax = max((p for p, _ in env), default=0.0)
    tol = max(tol_frac * pmax, 1.0)
    cand = [m for p, m in env if abs(p - P_d) <= tol]
    if not cand:
        cand = [m for p, m in env if (p - P_d) * (p - P_d) < 1e12]
    return max(cand) if cand else None


def verificar_rc(sec, env):
    """Bloque de verificacion RC de la seccion."""
    Ag = sec.Ag
    Ast = sec.Ast
    Pn0 = (0.85 * FC * (Ag - Ast) + FY * Ast) / 1e3     # kN
    cb, Pb, Mb = punto_balance(sec)
    cb_, Pb_, Mb_ = cb, Pb / 1e3, Mb / 1e6     # N -> kN, N·mm -> kN·m
    # flexa pura: punto de la envolvente con axial mas cercana a cero
    mn_cero = min(env, key=lambda t: abs(t[0]))
    Mn_puro = mn_cero[1]
    pb = (Pb_, Mb_)
    return {
        "Pn0_kN": round(Pn0, 1),
        "Pn0_max_permisible_08": round(0.8 * Pn0, 1),
        "balanceado": {
            "c_mm": round(cb_, 1), "Pb_kN": round(Pb_, 1),
            "Mb_kNm": round(Mb_, 1),
        },
        "flexion_pura_Mn_kNm": round(Mn_puro, 1),
        "Pn0_acero_apegado": round((0.85 * FC * Ag) / 1e3, 1),
        "rho_%": round(Ast / Ag * 100.0, 3),
        "formulas": {
            "Pn0": "0.85 f'c (Ag - Ast) + fy Ast",
            "cb": "eps_cu * d / (eps_cu + ey)",
            "nota": ("valores NOMINALES (sin factor phi); phi_ties=0.65, "
                     "phi_flexion segun ACI"),
        },
    }


def cargar_demandas():
    """Demandas (P, My, Mz) desde combinaciones.json (superposicion)."""
    ruta = os.path.join(SALIDAS, "combinaciones.json")
    with open(ruta, encoding="utf-8") as f:
        data = json.load(f)
    comp = data["comparacion"]
    demandas = {}
    targets = {"11020": "columna", "11033": "muro"}
    for combo, filas in comp.items():
        for elem in filas:
            tag = str(elem["elementTag"])
            if tag in targets:
                fila = {f["componente"]: f["superpuesto"] for f in elem["filas"]}
                d = demandas.setdefault(tag, {})
                d[combo] = {
                    "P": fila["P"],
                    "My": fila["My"],
                    "Mz": fila["Mz"],
                    "Mt": np.hypot(fila["My"], fila["Mz"]),
                }
    return demandas, targets


def graficar(sec, env, verif, dem, titulo, archivo, demanda_color="tab:red"):
    ag = sec.Ag / 1e6          # m2
    ast = sec.Ast / 1e6
    P0 = verif["Pn0_kN"]
    cb = verif["balanceado"]["c_mm"]
    Pb = verif["balanceado"]["Pb_kN"]
    Mb = verif["balanceado"]["Mb_kNm"]
    x = np.array([m for _, m in env])
    y = np.array([p for p, _ in env])
    fig, ax = plt.subplots(figsize=(8.5, 7))
    ax.plot(x / 1e3, y, "b-", lw=2.2, label=f"Envolvente P-M ({sec.nombre})")
    ax.axhline(0, color="k", lw=0.6)
    ax.axvline(0, color="k", lw=0.6)
    ax.plot(0, P0, "ko", ms=8, label=f"Pn0 = {P0:,.0f} kN (comp. pura)")
    ax.plot(Mb, Pb, "m*", ms=15,
            label=f"Balanceado (c={cb:,.0f} mm, Pb={Pb:,.0f} kN)")
    for etiqueta, punto in dem.items():
        P = punto["P"]
        M = punto["Mt"]
        ax.plot(M, P, "o", ms=9, color=demanda_color,
                label=f"Demanda {etiqueta}: P={P:,.0f} kN, M={M:,.0f} kN·m")
        ax.annotate(etiqueta, (M, P), textcoords="offset points",
                    xytext=(10, 8), fontsize=9, color=demanda_color)
    ax.set_xlabel("Momento nominal M [kN·m]")
    ax.set_ylabel("Carga axial P [kN]  (compresión +)")
    ax.set_title(f"{titulo}\n{sec.descripcion}\n"
                 f"Ag={ag:.3f} m², Ast={ast:.4f} m²")
    ax.legend(fontsize=8, loc="lower right")
    ax.grid(alpha=0.4)
    fig.tight_layout()
    ruta = os.path.join(FIG, archivo)
    fig.savefig(ruta, dpi=150)
    plt.close(fig)
    return ruta


def main():
    os.makedirs(SALIDAS, exist_ok=True)
    os.makedirs(FIG, exist_ok=True)

    col = COL70()
    muro = MUR20()

    env_col = curva_pm(col)
    env_mur = curva_pm(muro)
    vcol = verificar_rc(col, env_col)
    vmur = verificar_rc(muro, env_mur)

    demandas, _ = cargar_demandas()

    # ----- demanda-capacidad columna 11020 -----
    dc = {"columna_11020": {}, "muro_11033": {}}
    dcol = demandas.get("11020", {})
    for combo in ("U1", "U2", "U3", "U4"):
        if combo not in dcol:
            continue
        P = dcol[combo]["P"]
        M = dcol[combo]["Mt"]
        cap = capacidad_a_P(env_col, max(P, 0.0))
        dcr_solo_M = (M / cap) if cap else None
        # chequeo axial
        racio_axial = P / vcol["Pn0_kN"]
        dc["columna_11020"][combo] = {
            "P_kN": round(P, 1), "My_kNm": round(dcol[combo]["My"], 1),
            "Mz_kNm": round(dcol[combo]["Mz"], 1),
            "M_efectivo_kNm": round(M, 1),
            "M_nom_capacidad_kNm": round(cap, 1) if cap else None,
            "DCR_M": round(dcr_solo_M, 3) if cap else None,
            "P_Pn0": round(racio_axial, 3),
        }

    dmur = demandas.get("11033", {})
    for combo in ("U3", "U4"):
        if combo not in dmur:
            continue
        P = dmur[combo]["P"]
        M = dmur[combo]["Mt"]
        cap = capacidad_a_P(env_mur, max(P, 0.0))
        dcr = (M / cap) if cap else None
        dc["muro_11033"][combo] = {
            "P_kN": round(P, 1), "My_kNm": round(dmur[combo]["My"], 1),
            "Mz_kNm": round(dmur[combo]["Mz"], 1),
            "M_efectivo_kNm": round(M, 1),
            "M_nom_capacidad_kNm": round(cap, 1) if cap else None,
            "DCR_M": round(dcr, 2) if cap else None,
        }

    fig_col = graficar(col, env_col, vcol, dcol, "Columna COL_70 (eje F-2)",
                       "pm_columna.png")
    fig_mur = graficar(muro, env_mur, vmur,
                       {k: v for k, v in dmur.items() if k in ("U3", "U4")},
                       "Muro del nucleo MUR_20 (1 m)", "pm_muro.png")

    resumen = {
        "secciones": {
            "columna": {"nombre": col.nombre, "descripcion": col.descripcion},
            "muro": {"nombre": muro.nombre, "descripcion": muro.descripcion},
        },
        "verificacion_rc": {
            "columna": vcol,
            "muro": vmur,
            "criterios": {
                "tipo_material": "G35 (f'c=35) / A63-42H (fy=420)",
                "eps_cu": EPSCU, "eps0": EPS0, "ey": round(EY, 5),
                "eps_su": ES_SU,
                "modelo_hormigon": (
                    "parabola Hognestad + descenso lineal a 0.85 f'c, "
                    "sin tracciòn"),
                "modelo_acero": ("elastico-perfectamente plastico con "
                                 "endurecimiento 0.5% Es"),
                "nota": "valores nominales; DCR usa M_nom sin phi",
            },
        },
        "demanda_capacidad": dc,
        "figuras": {"columna": fig_col, "muro": fig_mur},
        "criterios_grupo": [
            "Armadura de la columna ASUMIDA 12phi25 (rho=1.20%), pendiente "
            "de confrontar con planos.",
            "El muro se evalua con la seccion unitaria MUR_20 (1 m x 0.2 m, "
            "2 capas phi12@200): la DCR > 1 no implica deficit real de la "
            "pared completa, sino que la representacion por unidad de "
            "longitud debe reemplazarse por la longitud real del muro.",
            "El momento efectivo de la columna es M_t = sqrt(My^2 + Mz^2) "
            "(primer aproximación biaxial sobre la envolvente uniaxial).",
        ],
    }

    with open(os.path.join(SALIDAS, "curvas_pm.json"), "w",
              encoding="utf-8") as f:
        json.dump(resumen, f, ensure_ascii=False, indent=2)

    print("=" * 80)
    print("CURVAS P-M  |  verificacion RC y demanda-capacidad")
    print("  Columna:", col.descripcion)
    v = vcol
    print(f"    Pn0={v['Pn0_kN']:.0f} kN | 0.8Pn0={v['Pn0_max_permisible_08']:.0f} kN"
          f" | balanceado (c={v['balanceado']['c_mm']:.0f}, "
          f"Pb={v['balanceado']['Pb_kN']:.0f} kN, "
          f"Mb={v['balanceado']['Mb_kNm']:.0f} kN·m) | "
          f"Mn,puro={v['flexion_pura_Mn_kNm']:.0f} kN·m")
    print("  Muro:", muro.descripcion)
    v = vmur
    print(f"    Pn0={v['Pn0_kN']:.0f} kN | 0.8Pn0={v['Pn0_max_permisible_08']:.0f} kN"
          f" | balanceado (c={v['balanceado']['c_mm']:.0f}, "
          f"Pb={v['balanceado']['Pb_kN']:.0f} kN, "
          f"Mb={v['balanceado']['Mb_kNm']:.0f} kN·m) | "
          f"Mn,puro={v['flexion_pura_Mn_kNm']:.0f} kN·m")
    print("  Demanda-capacidad:")
    for tag, combos in dc.items():
        for c, r in combos.items():
            print(f"    {tag:>18} {c}: P={r['P_kN']:>8.1f} kN  "
                  f"M_ef={r['M_efectivo_kNm']:>8.1f} kN·m  "
                  f"cap={r['M_nom_capacidad_kNm']:>8.1f}  "
                  f"DCR={r['DCR_M'] if 'DCR_M' in r else 'n/a'}")
    print("  Figuras:", fig_col)
    print(f"           {fig_mur}")
    print("=" * 80)


if __name__ == "__main__":
    main()
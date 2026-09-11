"""
P1A3 - Momento-curvatura de una seccion representativa (columna COL_70).

Implementa el analisis de seccion por FIBRAS (equivalente a 'section Fiber'
de OpenSees) en Python puro: compatibilidad de deformaciones, equilibrio de
fuerzas axiales y momento resultante.

Secciones:
  - COL_70 0.70 m x 0.70 m con 12phi25 (rho ~= 1.20%).
    Barras: 4 en cara superior, 4 en cara inferior y 4 laterales al eje
    neutro (z = profundidad desde la fibra extrema en compresion).

Materiales (unidades: versos MPa / N / mm):
  - Hormigon G35: f'c = 35 MPa, eps0 = 0.002, eps_cu = 0.003
      rama ascendente parabolica Hognestad y descenso lineal a 0.85 f'c.
  - Acero A63-42H: fy = 420 MPa, Es = 200 GPa, ey = 0.0021,
      endurecimiento al 0.5% de Es a partir de eps_sh = 0.01 hasta eps_su = 0.09.

Curva M-phi bajo carga axial fija P: se impone equilibrio N(c) = P y se
barre la curvatura phi hasta el criterio de termino (ver abajo).

Criterio de termino (documentado en el reporte):
    phi_u = min{ phi | eps_c(fibra extrema comprimida) >= eps_cu=0.003
                             OR  eps_s(barra mas traccionada) <= -eps_su=0.09 }
Es decir, falla por aplastamiento del hormigon o fractura del acero.

Rigidez inicial: EI0 = pendiente dM/dphi en el tramo elastico (fit lineal de
los primeros puntos) y comparacion con EIgg = Ec*Ig (seccion bruta).

Sensibilidad de discretizacion: se repite el M-phi variando el numero de
fibras de hormigon (10, 20, 40, 80) y se compara M_u y phi_u.

Salidas en reports/salidas/momento_curvatura.json
Figuras en reports/fig/mf_curva.png
"""
import json
import os
import sys

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SALIDAS = os.path.join(RAIZ, "reports", "salidas")
FIG = os.path.join(RAIZ, "reports", "fig")

# Materiales [MPa]
FC = 35.0
EPS0 = 0.002
EPSCU = 0.003
FY = 420.0
ES = 200000.0
EY = FY / ES
EPS_SH = 0.01
ES_SU = 0.09
ESH_MOD = 0.005 * ES          # modulo de endurecimiento (0.5% Es)


def sig_con(eps):
    """Hormigon no confinado: parabolica + descenso lineal (Hognestad)."""
    if eps <= 0.0:
        return 0.0
    if eps <= EPS0:
        return FC * (2.0 * eps / EPS0 - (eps / EPS0) ** 2)
    if eps <= EPSCU:
        return FC * (1.0 - 0.15 * (eps - EPS0) / (EPSCU - EPS0))
    return 0.85 * FC   # meseta (poco usada: PM/phi se cortan en eps_cu)


def sig_acero(eps):
    """Acero bilineal con endurecimiento (simetrico traccion/compresion)."""
    def _b(branch):           # branch = |eps|
        if branch <= EY:
            return ES * branch
        if branch <= EPS_SH:
            return FY
        if branch <= ES_SU:
            return FY + ESH_MOD * (branch - EPS_SH)
        return FY + ESH_MOD * (ES_SU - EPS_SH)
    return _b(abs(eps)) * (1.0 if eps >= 0 else -1.0)


class Seccion:
    def __init__(self, nombre, B, H, barras, descripcion):
        self.nombre = nombre
        self.B = B                  # ancho (mm) -> hormigon
        self.H = H                  # canto (mm)
        self.barras = barras        # lista de (As_mm2, z_mm)
        self.Ast = sum(a for a, _ in barras)
        self.Ag = B * H
        self.descripcion = descripcion

    def comprende(self):
        """Compacta barras iguales para barrido (opcional, no usado)."""
        return self


def COL70():
    """Columna 0.70x0.70 con 12phi25 (rho=1.20%)."""
    B = H = 700.0
    as25 = np.pi * 25.0 ** 2 / 4.0          # 490.9 mm2
    rec = 40.0                              # recubrimiento al eje de la barra
    barras = []
    # cara superior (compresion) 4phi25
    for _ in range(4):
        barras.append((as25, rec))
    # cara inferior 4phi25
    for _ in range(4):
        barras.append((as25, H - rec))
    # laterales (sobre el eje neutro para flexion fuerte) 4phi25
    for _ in range(4):
        barras.append((as25, H / 2.0))
    return Seccion("COL_70", B, H, barras,
                   "0.70x0.70 m, 12phi25 (4+4+4), rec. 40 mm, rho=1.20%")


def MUR20():
    """Muro e=20 cm, largo L=1.0 m: 2 capas de phi12@200 verticales."""
    B, H = 200.0, 1000.0                    # ancho (espesor) x canto (largo)
    as12 = np.pi * 12.0 ** 2 / 4.0          # 113.1 mm2
    # 5 estaciones a 100,300,500,700,900 mm; cada una 2 phi12 (2 capas)
    barras = [(2 * as12, z) for z in (100, 300, 500, 700, 900)]
    return Seccion("MUR_20", B, H, barras,
                   "muro e=20 cm, L=1.0 m, 2 capas phi12@200, rho_l=0.565%")


def integrar_fibras(sec, strips, phi, c):
    """Fuerzas (P, M) para una DEFORMADA dada: eps(z) = phi*(c - z).

    Se usa en la curva M-phi (phi impuesto, c resuelto por equilibrio axial).
    """
    H = sec.H
    P = M = 0.0
    for z_c, dz, b in strips:
        eps = phi * (c - z_c)
        sig = sig_con(eps)
        A = b * dz
        P += sig * A
        M += sig * A * (H / 2.0 - z_c)
    for As, zb in sec.barras:
        eps = phi * (c - zb)
        sig = sig_acero(eps)
        P += sig * As
        M += sig * As * (H / 2.0 - zb)
    return P, M


def integrar_pm(sec, strips, c):
    """Fuerzas (P, M) de CAPACIDAD con eje neutro a profundidad c [mm].

    c medido desde la fibra extrema en compresion (z=0) hacia abajo.
    Deformacion por compatibilidad: eps(z) = phi*(c - z). Se impone la
    deformacion controlante (aplastamiento del hormigon eps_cu o fractura
    del acero eps_su), por lo que esta funcion genera la envolvente P-M.
    Convencion: P compresion (+), M respecto del centroide (z - H/2).
    """
    H = sec.H
    # 1) Determinar la deformacion controlante
    zs = [uz for _, uz in sec.barras]
    z_most = max(zs)                       # barra mas traccionada (abajo)
    if c > 0.0:
        et = EPSCU                          # aplastamiento del hormigon
        for _, zb in sec.barras:
            if et * (c - zb) / c < -ES_SU:
                # el acero de la barra 'zb' se lleva el control
                et = -ES_SU * c / (c - zb)
    else:
        # eje neutro fuera de la seccion: controla el acero mas traccionado
        et = -ES_SU * c / (c - z_most)
    phi = et / c if c != 0.0 else 0.0
    P = M = 0.0
    for z_c, dz, b in strips:
        eps = phi * (c - z_c)
        sig = sig_con(eps)
        A = b * dz
        P += sig * A
        M += sig * A * (H / 2.0 - z_c)
    for As, zb in sec.barras:
        eps = phi * (c - zb)
        sig = sig_acero(eps)
        P += sig * As
        M += sig * As * (H / 2.0 - zb)
    return P, M


def puntos_pm(sec, n_strips=64, c_max=None):
    """Barrido de P-M: P desde compresion pura hasta traccion pura."""
    H = sec.H
    if c_max is None:
        c_max = 1.6 * H
    strips = _strips(sec, n_strips)

    # Punto de compresion pura (ACI 22.4.2.2)
    P0 = 0.85 * FC * (sec.Ag - sec.Ast) + FY * sec.Ast   # N
    pts = [(P0, 0.0, 0.0, "compresion pura (Pn0)")]

    zb = max(z for _, z in sec.barras)
    cb = EPSCU * zb / (EPSCU + EY)                        # ACI 22.2.7: balanceado
    cs = np.unique(np.concatenate([
        np.linspace(c_max, cb, 500),                       # compression -> balanceado
        np.linspace(cb, -c_max, 500),                      # balanceado -> traccion
    ]))[::-1]
    for c in cs:
        P, M = integrar_pm(sec, strips, c)
        pts.append((P, abs(M), c, "barrido"))

    # Punto de traccion pura
    T = -FY * sec.Ast
    pts.append((T, 0.0, -1e9, "traccion pura"))
    return pts, strips


def punto_balance(sec):
    """Punto balanceado ACI: c_b = eps_cu*d/(eps_cu + ey); (Pb, Mb)."""
    zb = max(z for _, z in sec.barras)
    cb = EPSCU * zb / (EPSCU + EY)
    strips = _strips(sec, 64)
    P, M = integrar_pm(sec, strips, cb)
    return cb, P, abs(M)


def _strips(sec, n):
    return [(z, sec.H / n, sec.B)
            for z in np.arange(sec.H / n / 2.0, sec.H, sec.H / n)]


def momento_curvatura(sec, P_kN, n_strips=64, n_phi=500):
    """Barrido de curvatura bajo carga axial fija P (kN).

    Devuelve dict con arrays phi[rad/m], M[kN*m], y los indices del primer
    fluencia y del termino (phi_u, M_u) con su causa.
    """
    P_target = P_kN * 1e3              # kN -> N
    strips = _strips(sec, n_strips)
    H = sec.H
    phis_m = np.geomspace(1e-4, 6e-2, 700)   # rad/m (fino cerca del origen)
    Ms, c_sol = [], []
    causa = None
    for phi_m in phis_m:
        phip = phi_m * 1e-3             # 1/mm (compatibilidad en mm)
        lo, hi = -3.0 * H, 3.0 * H
        for _ in range(200):
            cmid = 0.5 * (lo + hi)
            P, _ = integrar_fibras(sec, strips, phip, cmid)
            if P > P_target:
                hi = cmid
            else:
                lo = cmid
        c = 0.5 * (lo + hi)
        _, Mc = integrar_fibras(sec, strips, phip, c)
        Ms.append(Mc / 1e6)            # N*mm -> kN*m
        c_sol.append(c)

        # criterios de termino
        et = phip * c                    # def. fibra extrema comprimida
        es_min = min(phip * (c - zb) for _, zb in sec.barras)
        if et >= EPSCU:
            causa = "aplastamiento hormigon (eps_c >= eps_cu)"
            phis_m = phis_m[:len(Ms)]
            Ms = Ms[:len(Ms)]
            c_sol = c_sol[:len(Ms)]
            break
        if es_min <= -ES_SU:
            causa = "fractura acero (eps_s <= -eps_su)"
            phis_m = phis_m[:len(Ms)]
            Ms = Ms[:len(Ms)]
            c_sol = c_sol[:len(Ms)]
            break
    if causa is None:
        causa = "sin termino (phi_max alcanzado)"
    Ms = np.array(Ms)
    c_sol = c_sol[:len(Ms)]

    # primer fluencia del acero o del hormigon
    iy = None
    for i, c in enumerate(c_sol):
        if c is None:
            continue
        et = phis_m[i] * 1e-3 * c
        es_min = min(phis_m[i] * 1e-3 * (c - zb) for _, zb in sec.barras)
        if abs(es_min) >= EY or et >= EPS0:
            iy = i
            break

    # rigidez inicial EI0 = pendiente M-phi en el tramo elastico inicial
    # (M en kN*m, phi en rad/m -> pendiente en kN*m2)
    i1 = next((i for i, m in enumerate(Ms) if m != 0.0), None)
    if i1 is not None:
        ei0 = abs(Ms[i1]) / phis_m[i1]
    else:
        ei0 = None
    sec.Ig = sec.B * sec.H ** 3 / 12.0      # mm4
    EIgg = 4700.0 * np.sqrt(FC) * sec.Ig    # Ec=4700*sqrt(fc) MPa -> N*mm2
    E0 = 2.0 * FC / EPS0                    # tangente parabola en el origen
    return {
        "P_kN": P_kN,
        "phi": phis_m.tolist(),             # rad/m
        "M_kNm": Ms.tolist(),
        "i_primera_fluencia": iy,
        "causa_termino": causa,
        "EI0_kNm2": round(ei0, 1) if ei0 is not None else None,
        "EIgg_kNm2": round(EIgg / 1e9, 1),
        "E0_parabola_MPa": round(E0, 1),
        "relacion_EI0_EIgg": round(ei0 / (EIgg / 1e9), 3) if ei0 else None,
        "relacion_EI0_E0Ig": round(ei0 / (E0 * sec.Ig / 1e9), 3)
        if ei0 else None,
        "nota_EI": ("EIgg se calcula con Ec=4700*sqrt(f'c)=27806 MPa (modulo "
                    "usado en el modelo); EI0 del analisis por fibras usa el "
                    "tangente de la parabola E0=2 f'c/eps0 = 35000 MPa"),
    }


def main():
    os.makedirs(SALIDAS, exist_ok=True)
    os.makedirs(FIG, exist_ok=True)
    col = COL70()
    wall = MUR20()

    cargas_axiales = {"P0": 0.0, "P2500": 2500.0, "P5513": 5513.0}
    curvas = {}
    for tag, P in cargas_axiales.items():
        curvas[tag] = momento_curvatura(col, P)

    # punto de termino (phi_u, M_u) para P=5513
    base = curvas["P5513"]
    phi_u = base["phi"][-1]
    M_u = base["M_kNm"][-1]
    iy = base["i_primera_fluencia"]
    phi_y = base["phi"][iy] if iy else None
    M_y = base["M_kNm"][iy] if iy else None

    # sensibilidad de discretizacion
    sens = []
    for n in (10, 20, 40, 80):
        r = momento_curvatura(col, 5513.0, n_strips=n, n_phi=500)
        sens.append({
            "n_strips": n, "M_u_kNm": round(r["M_kNm"][-1], 1),
            "phi_u": round(r["phi"][-1], 6),
            "causa": r["causa_termino"],
        })
    sens_ref = sens[-1]["M_u_kNm"]
    for s in sens:
        s["dif_Mu_pct_vs_80"] = round(
            (s["M_u_kNm"] - sens_ref) / sens_ref * 100.0, 3)

    resumen = {
        "seccion": {"nombre": col.nombre, "descripcion": col.descripcion,
                    "Ag_mm2": col.Ag, "Ast_mm2": col.Ast,
                    "rho_%": round(col.Ast / col.Ag * 100.0, 3)},
        "materiales": {
            "hormigon_fc_MPa": FC, "eps0": EPS0, "eps_cu": EPSCU,
            "acero_fy_MPa": FY, "Es_MPa": ES, "ey": round(EY, 5),
            "eps_sh": EPS_SH, "eps_su": ES_SU,
            "nota_curva_H": "Hognestad parabola+descenso lineal a 0.85 f'c",
            "nota_acero": "bilineal con endurecimiento 0.5% Es",
        },
        "momento_curvatura_por_carga_axial": {
            k: {
                "P_kN": v["P_kN"], "phi_u": v["phi"][-1],
                "M_u_kNm": round(v["M_kNm"][-1], 1),
                "causa_termino": v["causa_termino"],
                "EI0_kNm2": v["EI0_kNm2"], "EIgg_kNm2": v["EIgg_kNm2"],
                "EI0_EIgg": v["relacion_EI0_EIgg"],
                "EI0_E0Ig": v["relacion_EI0_E0Ig"],
            } for k, v in curvas.items()
        },
        "carga_axial_representativa": {
            "justificacion": "P de la columna mas cargada (F-2, elem. 11020) "
                             "con combinacion 1.2G+1.6Q",
            "P_kN": 5513.0,
        },
        "primer_fluencia_P5513": {
            "phi_y": phi_y, "M_y_kNm": round(M_y, 1),
        },
        "termino_P5513": {"phi_u": phi_u, "M_u_kNm": round(M_u, 1),
                          "causa": base["causa_termino"]},
        "sensibilidad_discretizacion": sens,
        "criterio_termino": (
            "phi_u = min{ phi | eps_c(fibra extrema comprimida) >= 0.003 "
            "(aplastamiento hormigon)  OR  eps_s(barra mas traccionada) "
            "<= -0.09 (fractura acero) }"
        ),
    }

    # --- Figura ----------------------------------------------------------
    fig, ax = plt.subplots(1, 2, figsize=(13, 5.2))
    for tag, P in cargas_axiales.items():
        v = curvas[tag]
        lbl = f"P = {P:,.0f} kN"
        ax[0].plot(v["phi"], v["M_kNm"], lw=2, label=lbl)
    if iy:
        ax[0].plot(phi_y, M_y, "gs", ms=7, label="primer fluencia (P=5513)")
    ax[0].plot(phi_u, M_u, "rs", ms=8, label="termino / phi_u (P=5513)")
    ax[0].set_xlabel(r"curvatura  $\phi$  [1/m]")
    ax[0].set_ylabel("Momento M [kN·m]")
    ax[0].set_title("Momento-Curvatura  COL_70 (70x70, 12φ25)")
    ax[0].legend(fontsize=8)
    ax[0].grid(alpha=0.4)

    ss = sens
    ax[1].bar([str(s["n_strips"]) for s in ss],
              [s["dif_Mu_pct_vs_80"] for s in ss], color="steelblue")
    ax[1].axhline(0, color="k", lw=0.8)
    ax[1].set_xlabel("nº fibras de hormigón")
    ax[1].set_ylabel("ΔM_u vs. 80 fibras [%]")
    ax[1].set_title("Sensibilidad de la discretización (P=5513 kN)")
    ax[1].grid(alpha=0.4)
    fig.tight_layout()
    ruta_fig = os.path.join(FIG, "mf_curva.png")
    fig.savefig(ruta_fig, dpi=150)
    plt.close(fig)

    with open(os.path.join(SALIDAS, "momento_curvatura.json"), "w",
              encoding="utf-8") as f:
        json.dump(resumen, f, ensure_ascii=False, indent=2)

    print("=" * 78)
    print("MOMENTO-CURVATURA  |  COL_70 (0.70x0.70, 12phi25)")
    print(f"  rho = {resumen['seccion']['rho_%']:.2f}%   "
          f"Ast = {col.Ast:.0f} mm2")
    for k, v in resumen["momento_curvatura_por_carga_axial"].items():
        print(f"  P={v['P_kN']:>6.0f} kN  M_u={v['M_u_kNm']:>7.1f} kN·m  "
              f"phi_u={v['phi_u']:.5f}  EI0/EIgg={v['EI0_EIgg']}  "
              f"[{v['causa_termino']}]")
    print(f"  Primer fluencia (P=5513): M_y={M_y:.1f} kN·m, "
          f"phi_y={phi_y:.5f}")
    print("  Sensibilidad:")
    for s in sens:
        print(f"    n={s['n_strips']:>3}  M_u={s['M_u_kNm']:>7.1f} "
              f"phi_u={s['phi_u']:.6f}  dif={s['dif_Mu_pct_vs_80']:+.3f}%")
    print(f"  Figura : {ruta_fig}")
    print("=" * 78)


if __name__ == "__main__":
    main()
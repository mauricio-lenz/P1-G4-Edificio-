"""
P1A4 - Exporta TODO lo que Unity necesita como postprocesador estructural:
geometria (losas + elementos), secciones/materiales, ejes locales,
restricciones, apoyos, areas tributarias, cargas, y RESULTADOS por
combinacion (U1..U4) para 390 elementos + deformada por nodo + curvas P-M
y demanda-capacidad de la columna 11020 y el muro 11033.

Lee:
- modelo_edificio_geometria.json      -> nodos + losas (huella + voladizos)
- secciones_verticales.json           -> secciones (dimensiones, inercias, mat)
- elementos_verticales.json           -> columnas y muros
- vigas_tributarias.json              -> vigas + areas tributarias por nivel
- reports/salidas/casos_base_{G,Q,EX,EY}.json -> esfuerzos N,Vy,Vz,My,Mz
                                                  y desplazamientos por caso
- reacciones_base_edificio.json       -> apoyos de la base
- reports/salidas/curvas_pm.json      -> demanda-capacidad verificado (P1A3)

Genera: edificio_para_unity.json
"""
import json
import math
import os
import sys

RAIZ = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(RAIZ, "scripts"))

def leer(r):
    with open(r, encoding="utf-8") as f:
        return json.load(f)

# ----------------------------------------------------------------- combinaciones
COMBOS = {
    "U1": {"G": 1.4},
    "U2": {"G": 1.2, "Q": 1.6},
    "U3": {"G": 1.2, "Q": 1.0, "EX": 1.0},
    "U4": {"G": 0.9, "EY": 1.0},
}

# ------------------------------------------------------------------ vectores 3D
def sub(a, b):
    return [a[0] - b[0], a[1] - b[1], a[2] - b[2]]

def norm(v):
    return math.sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2])

def unit(v):
    n = norm(v)
    return [0.0, 0.0, 1.0] if n < 1e-12 else [v[i] / n for i in range(3)]

def cross(a, b):
    return [a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0]]

def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]

def ejes_locales(i, j):
    """Ejes locales del elemento: eX a lo largo del miembro; eZ vertical
    (para muros/columnas) o segun la direccion del plano; eY = eZ x eX."""
    ex = unit(sub(j, i))
    ref = [0.0, 0.0, 1.0] if abs(ex[2]) < 0.999 else [1.0, 0.0, 0.0]
    ey = unit(cross(ref, ex))
    ez = cross(ex, ey)
    return {"x": ex, "y": ey, "z": ez}


def main():
    geo = leer(os.path.join(RAIZ, "modelo_edificio_geometria.json"))
    secs = leer(os.path.join(RAIZ, "secciones_verticales.json"))["secciones"]
    verts = leer(os.path.join(RAIZ, "elementos_verticales.json"))
    vv = leer(os.path.join(RAIZ, "vigas_tributarias.json"))["vigas"]
    n_niv = geo["_meta"]["n_niveles"]
    coords = {n["nodeTag"]: [n["x"], n["y"], n["z"]] for n in geo["nodos"]}
    nodo_tipo = {n["nodeTag"]: n.get("tipo", "") for n in geo["nodos"]}

    # ---------- casos base: esfuerzos por elemento + desplazamientos ----------
    casos = {}
    for caso in ("G", "Q", "EX", "EY"):
        d = leer(os.path.join(RAIZ, "reports", "salidas",
                              f"casos_base_{caso}.json"))
        casos[caso] = {
            "elementos": {str(e["elementTag"]): e for e in d["elementos"]},
            "desplazamientos": d.get("desplazamientos", {}),  # por nivel
        }

    # ---------- tabla de secciones y materiales ----------
    secciones = {}
    materiales = {}
    for s in secs:
        mat = s.get("matTag", 1)
        if mat not in materiales:
            materiales[str(mat)] = {
                "nombre": "G35", "fc_MPa": 35.0, "fy_MPa": 420.0,
                "E_kPa": s.get("E_kPa"), "G_kPa": s.get("G_kPa"),
                "Es_kPa": 2.0e8,
            }
        secciones[str(s["sectionTag"])] = {
            "tag": s["sectionTag"], "nombre": s["nombre"], "tipo": s["tipo"],
            "matTag": mat, "b_m": s.get("b_m") or s.get("t_m"),
            "h_m": s.get("h_m") or s.get("L_m"),
            "A_m2": s["A_m2"], "Iy_m4": s["Iy_m4"], "Iz_m4": s["Iz_m4"],
            "J_m4": s["J_m4"], "plano_fuente": s.get("plano_fuente", ""),
        }

    # ---------- reetiquetado de elementos (mismo esquema que resultados) -------
    def tags_vertical(base, tipo):
        return {k: 10000 + k * 1000 + base for k in range(1, n_niv + 1)}

    nX = len([v for v in vv if v["dir"] == "X"])
    tag_map = {}          # (tipo, nivel, base_idx) -> elementTag
    for e in verts:
        base = e["iNode"]
        for k in range(1, n_niv + 1):
            tag_map[(e["type"], k, base)] = 10000 + k * 1000 + base
    for idx, v in enumerate(vv):
        grp = "X" if v["dir"] == "X" else "Y"
        i_grp = idx if grp == "X" else (idx - nX)
        for k in range(1, n_niv + 1):
            tag_map[("viga", k, (grp, i_grp))] = (
                (20000 if grp == "X" else 30000) + k * 100 + i_grp)

    # ---------- construir lista UNICA de elementos (390) ----------
    elementos = []
    for e in verts:
        base = e["iNode"]
        a0 = coords[base]
        prev = a0
        for k in range(1, n_niv + 1):
            p = coords[1000 + (k - 1) * 100 + base]
            tag = tag_map[(e["type"], k, base)]
            prev = p[:]
            elementos.append(_hacer_elemento(
                tag, e["type"], base, 1000 + (k - 1) * 100 + base,
                e["sectionTag"], k, e["descripcion"], a0, p))
            a0 = p[:]
    for idx, v in enumerate(vv):
        grp = "X" if v["dir"] == "X" else "Y"
        i_grp = idx if grp == "X" else (idx - nX)
        for k in range(1, n_niv + 1):
            a = coords[v["iNode"] + (k - 1) * 100]
            b = coords[v["jNode"] + (k - 1) * 100]
            tag = tag_map[("viga", k, (grp, i_grp))]
            elementos.append(_hacer_elemento(
                tag, "viga", v["iNode"] + (k - 1) * 100,
                v["jNode"] + (k - 1) * 100, v["sectionTag"], k,
                f"Viga {v['dir']} L={v['longitud_m']:g} m · nivel {k}",
                a, b, _trib=v, _dir=grp))

    # ---------- esfuerzos por combinacion --------------
    for el in elementos:
        res = {}
        for nombre, pesos in COMBOS.items():
            v = {"P": 0.0, "Vy": 0.0, "Vz": 0.0, "T": 0.0, "My": 0.0, "Mz": 0.0,
                 "Vyj": 0.0, "Vzj": 0.0}
            for caso, w in pesos.items():
                cc = casos[caso]["elementos"].get(str(el["elementTag"]))
                if cc:
                    for k in ("P", "Vy", "Vz", "My", "Mz", "Vyj", "Vzj"):
                        v[k] += w * cc[k]
            res[nombre] = {
                "N": round(v["P"], 2), "Vy": round(v["Vy"], 2),
                "Vz": round(v["Vz"], 2), "T": 0.0,
                "My": round(v["My"], 2), "Mz": round(v["Mz"], 2),
            }
        el["resultados"] = res

    # ---------- deformada (diafragma rigido por nivel, por combo) ----------
    niveles = list(geo["_meta"]["z_niveles_m"].values())
    deformada = {c: {} for c in COMBOS}
    master_pts = {}
    for k in range(1, n_niv + 1):
        mt = 9500 + k
        master_pts[k] = (coords[mt][0], coords[mt][1]) if mt in coords else \
            (0.0, 0.0)
    for nodo in geo["nodos"]:
        t = nodo["nodeTag"]
        z = nodo["z"]
        k = min(range(1, n_niv + 1), key=lambda kk: abs(
            (niveles[kk - 1] if kk - 1 < len(niveles) else z) - z))
        for nombre, pesos in COMBOS.items():
            cum = [0.0, 0.0, 0.0]
            for caso, w in pesos.items():
                niv = casos[caso]["desplazamientos"].get(str(k))
                if not niv:
                    continue
                x0, y0 = master_pts[k]
                dx = nodo["x"] - x0
                dy = nodo["y"] - y0
                ux = niv.get("ux_master", 0.0) - niv.get("rz_master", 0.0) * dy
                uy = niv.get("uy_master", 0.0) + niv.get("rz_master", 0.0) * dx
                uz = niv.get("uz_master", 0.0)
                cum[0] += w * ux
                cum[1] += w * uy
                cum[2] += w * uz
            deformada[nombre][str(t)] = [round(cum[0], 5), round(cum[1], 5),
                                         round(cum[2], 5)]

    # ---------- apoyos ----------
    reac = leer(os.path.join(RAIZ, "reacciones_base_edificio.json"))
    apoyos = []
    for r in reac["reacciones"]:
        apoyos.append({
            "nodeTag": r["nodeTag"],
            "x": r["x_cm"] / 100.0, "y": r["y_cm"] / 100.0,
            "z": r["z_cm"] / 100.0,
            "reacciones": {kk: round(vv, 2) for kk, vv in r["R_kn"].items()},
            "descripcion": r["descripcion"],
        })

    # ---------- nodos con restricciones ----------
    nodos = []
    for n in geo["nodos"]:
        empotrado = n.get("tipo") == "base"
        maestro = n["nodeTag"] >= 9500
        nodos.append({
            "nodeTag": n["nodeTag"], "x": n["x"], "y": n["y"], "z": n["z"],
            "tipo": n.get("tipo", "slave"),
            "restrained": [1, 1, 1, 1, 1, 1] if empotrado else [0, 0, 0, 0, 0, 0],
            "es_maestro_diafragma": maestro,
        })

    # ---------- cargas (texto para el inspector) ----------
    cargas_piso = leer(os.path.join(RAIZ, "cargas_piso.json"))
    cargas = {
        "G_kN_m2": cargas_piso["losa"]["pp_losa_kN_m2"] +
                   cargas_piso["cargas"]["pm_adic_kN_m2"],
        "Q_kN_m2": cargas_piso["cargas"]["sc_kN_m2"],
        "psi_carga_viva": 0.25,
        "qG_nominal_kN_m2": cargas_piso["qG_kN_m2"],
        "a_piso_m2": cargas_piso["a_piso_m2"],
        "sismo": {
            "C_adoptado": 0.25, "V0_kN": 11824.3,
            "f_por_nivel_kN": [788.3, 1576.6, 2364.9, 3153.1, 3941.4],
        },
    }

    # ---------- areas tributarias ----------
    areas = []
    for v in vv:
        for k in range(1, n_niv + 1):
            areas.append({
                "elementTag": tag_map[("viga", k, (v["dir"], idx_in_grp(v, vv, nX)))],
                "tipo": "viga", "area_trib_m2": v["area_trib_m2"],
                "dir": v["dir"], "nivel": k,
                "poligono": v.get("poligono", []),
                "wG_kN_m": round(v["area_trib_m2"] * cargas["G_kN_m2"] /
                                 v["longitud_m"], 3),
                "wQ_kN_m": round(v["area_trib_m2"] * cargas["Q_kN_m2"] /
                                 v["longitud_m"], 3),
            })

    # ---------- curva P-M + demanda-capacidad ----------
    demandas = _dc_block()

    # ---------- losas (malla) ----------
    losas = []
    for L in geo["losas"]:
        esc = L["esquinas"]
        idx = [0, 1, 2, 0, 2, 3]
        losas.append({
            "nivel": L["nivel"], "nombre": L["nombre"], "z_m": L["z_m"],
            "vertices": [[c["x"], c["y"], c["z"]] for c in esc],
            "triangulos": idx, "voladizos": L["voladizos"],
        })

    out = {
        "_meta": {
            "destino": "Unity",
            "version": "P1A4",
            "unidades": "metros (m); kN, kN·m",
            "coordenadas": {"nx": "E->I' (+X)", "ny": "3->1 (+Y)",
                            "nz": "vertical (+Z)"},
            "n_niveles": n_niv, "h_piso_m": geo["_meta"]["h_piso_m"],
            "z_niveles_m": niveles,
            "combos": list(COMBOS.keys()),
            "origen_z_base": 0.0,
            "trazabilidad": "elementTag OpenSees == elementTag de este JSON == "
                            "objeto Unity",
        },
        "materiales": materiales,
        "secciones": secciones,
        "nodos": nodos,
        "losas": losas,
        "elementos": elementos,
        "apoyos": apoyos,
        "areas_tributarias": areas,
        "cargas": cargas,
        "deformada": deformada,
        "demanda_capacidad": demandas,
    }
    with open(os.path.join(RAIZ, "edificio_para_unity.json"), "w",
              encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=2)

    n_el = len(elementos)
    n_mis = sum(1 for e in elementos if str(e["elementTag"]) not in
                casos["G"]["elementos"])
    print(f"Export P1A4 Unity: {n_el} elementos, secciones={len(secciones)}, "
          f"nodos={len(nodos)}, apoyos={len(apoyos)}")
    print(f"  sin resultados en casos_base (esperado 0): {n_mis}")


def idx_in_grp(v, vv, nX):
    for i, x in enumerate(vv):
        if x["elementTag"] == v["elementTag"]:
            if v["dir"] == "X":
                return i
            return i - nX
    return 0


def _hacer_elemento(tag, tipo, i_node, j_node, seccion, nivel, descripcion,
                    a, b, _trib=None, _dir=None):
    loc = ejes_locales(a, b)
    el = {
        "elementTag": tag,
        "type": tipo,
        "iNode": i_node,
        "jNode": j_node,
        "sectionTag": seccion,
        "matTag": 1,
        "nivel": nivel,
        "descripcion": descripcion,
        "a": [round(x, 4) for x in a],
        "b": [round(x, 4) for x in b],
        "ejes_locales": {
            "x": [round(x, 5) for x in loc["x"]],
            "y": [round(x, 5) for x in loc["y"]],
            "z": [round(x, 5) for x in loc["z"]],
        },
        "apoyado_base": i_node <= 51,
        "restricciones": ["empotrado en base" if i_node <= 51 else
                          "diafragma rigido del nivel"],
        "area_trib_m2": _trib.get("area_trib_m2") if _trib else None,
    }
    return el


def _dc_block():
    curvas_pm = leer(os.path.join(RAIZ, "reports", "salidas",
                                  "curvas_pm.json"))
    dc = curvas_pm.get("demanda_capacidad", {})
    verif = curvas_pm.get("verificacion_rc", {})
    # envolvente densa a partir del analizador de secciones (P1A3)
    curvas = {}
    try:
        from momento_curvatura import COL70, MUR20, puntos_pm
        for nombre, sec in (("columna", COL70()), ("muro", MUR20(4.7))):
            pts, _ = puntos_pm(sec)
            curve = [{"M_kNm": round(M / 1e6, 2), "P_kN": round(P / 1e3, 1)}
                     for P, M, c, l in pts if M > 0]
            curvas[nombre] = curve
    except Exception as exc:  # sin numpy/momento_curvatura
        curvas = {"nota": f"envolvente no embebida: {exc}"}
    return {
        "secciones_ref": {
            "columna": {"elementTag": 11020, "nombre": "COL_70"},
            "muro": {"elementTag": 11033, "nombre": "MUR_20 (panel L=4.7 m)"},
        },
        "verificacion": verif,
        "curva_pm": curvas,
        "demandas": {
            "columna": {c: r for c, r in dc.get("columna_11020", {}).items()},
            "muro": {c: r for c, r in dc.get("muro_11033", {}).items()},
        },
    }


if __name__ == "__main__":
    main()
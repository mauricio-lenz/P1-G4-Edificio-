using UnityEngine;

/// <summary>
/// Convención del proyecto (tutorial P1):
///   X_U = X_OS ; Y_U = Z_OS (arriba) ; Z_U = Y_OS (adelante)
/// Única conversión permitida entre OpenSees y Unity.
/// </summary>
public static class CoordinateMap
{
    public static Vector3 OsToUnity(float x, float y, float z)
    {
        return new Vector3(x, z, y);
    }

    public static Vector3 OsToUnity(Vector3 p)
    {
        return new Vector3(p.x, p.z, p.y);
    }

    public static Vector3 UnityToOs(Vector3 p)
    {
        return new Vector3(p.x, p.z, p.y);
    }
}
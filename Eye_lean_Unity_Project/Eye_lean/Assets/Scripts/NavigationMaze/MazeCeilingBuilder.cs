// SPDX-License-Identifier: MIT
using UnityEngine;

namespace EyeLean.NavigationMaze
{
    public class MazeCeilingBuilder : MonoBehaviour
    {
        private GameObject _ceiling;

        public void Build(float mazeSize, float ceilingHeight)
        {
            Destroy();
            _ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ceiling.name = "MazeCeiling";
            _ceiling.transform.SetParent(transform, false);
            _ceiling.transform.localPosition = new Vector3(
                mazeSize * 0.5f, ceilingHeight + 0.025f, mazeSize * 0.5f);
            _ceiling.transform.localScale = new Vector3(mazeSize, 0.05f, mazeSize);

            try { _ceiling.GetComponent<Renderer>().material = VRMaterialProvider.GetMaterial(new Color(0.25f, 0.25f, 0.28f)); }
            catch { _ceiling.GetComponent<Renderer>().material.color = new Color(0.25f, 0.25f, 0.28f); }

            var col = _ceiling.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);
        }

        public void Destroy()
        {
            if (_ceiling != null)
            {
                DestroyImmediate(_ceiling);
                _ceiling = null;
            }
        }
    }
}

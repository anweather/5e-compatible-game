using UnityEngine;

// Keeps a row's great tree (RowHouses' "great-tree" detail) standing on the drawn ground. A row is drawn before the
// valley has its ground, when every height reads as the flat grid's, and the ground eases as stamps reshape it; so the
// tree takes the ground's height under it each frame, one lookup a tree.
public class GroundedTree : MonoBehaviour
{
    ChemGrid grid;

    void LateUpdate()
    {
        if (grid == null) grid = ChemGrid.Current?.Grid;
        if (grid == null) return;
        var p = transform.position;
        float y = grid.GroundAt(p);
        if (!Mathf.Approximately(y, p.y)) transform.position = new Vector3(p.x, y, p.z);
    }
}

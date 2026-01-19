using UnityEngine;

public class HouseStateApplier : MonoBehaviour
{
    [SerializeField] private GameObject cube;
    [SerializeField] private GameObject sphere;
    [SerializeField] private GameObject capsule;
    [SerializeField] private GameObject cylinder;

    // 定義所有可能的狀態
    public enum HouseState { None, Cube, Sphere, All }

    public void ApplyState(HouseState state)
    {
        // 先全部關閉
        cube.SetActive(false);
        sphere.SetActive(false);
        capsule.SetActive(false);
        cylinder.SetActive(false);

        // 根據狀態開啟對應物件
        switch (state)
        {
            case HouseState.None:
                cylinder.SetActive(true);
                break;
            case HouseState.Cube:
                cube.SetActive(true);
                break;
            case HouseState.Sphere:
                sphere.SetActive(true);
                break;
            case HouseState.All:
                cube.SetActive(true);
                sphere.SetActive(true);
                capsule.SetActive(true);
                cylinder.SetActive(true);
                break;
        }
    }
}
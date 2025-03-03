using UnityEngine;
using System.Collections;

public class DelayedAction : MonoBehaviour
{
    public float delayTime = 2f; // 延遲時間（秒）
    public GameObject targetObject; // 需要執行動作的物體

    void Start()
    {
        // 開始延遲執行動作
        StartCoroutine(ExecuteAfterDelay());
    }

    IEnumerator ExecuteAfterDelay()
    {
        yield return new WaitForSeconds(delayTime);

        if (targetObject != null)
        {
            Debug.Log("延遲執行: " + targetObject.name);
            targetObject.SetActive(!targetObject.activeSelf); // 切換物件的顯示狀態
        }
    }
}

using UnityEngine;

public class ColorElementTrigger : MonoBehaviour
{
    [Header("����ޥ�")]
    [SerializeField] private Transform lid1; // Box3_lid
    [SerializeField] private Transform lid2; // Box3_lid (1)
    [SerializeField] private GameObject colorElement;

    [Header("�]�w�Ѽ�")]
    [Tooltip("Ĳ�o�Z�� (0.1 �N�� 10 ����)")]
    public float triggerDistance = 0.1f;
    [Tooltip("�Q�X���O�D�j�p")]
    public float popForce = 2.0f;

    private bool hasTriggered = false;
    private Rigidbody elementRb;

    void Start()
    {
        if (colorElement != null)
        {
            // ��l���A�]������
            colorElement.SetActive(false);
            // �w�����o Rigidbody
            elementRb = colorElement.GetComponent<Rigidbody>();
        }
    }

    void Update()
    {
        // �p�G�w�gĲ�o�L�A�Ϊ̤ޥο򥢡A�N���A�p��
        if (hasTriggered || lid1 == null || lid2 == null || colorElement == null) return;

        // 1. �p�����\�l�b�@�ɪŶ����Z��
        float currentDistance = Vector3.Distance(lid1.position, lid2.position);

        // 2. ���Z���p�� 10 ���� (0.1m) ��Ĳ�o
        if (currentDistance < triggerDistance)
        {
            TriggerPop();
        }
    }

    private void TriggerPop()
    {
        hasTriggered = true;

        // �Ұʪ���
        colorElement.SetActive(true);

        // 3. �I�[���z�O
        if (elementRb != null)
        {
            // �����V�W��Q�X�A�ña�@�I�I�H�����׬ݰ_�Ӥ���۵M
            Vector3 forceDirection = transform.up + new Vector3(Random.Range(-0.2f, 0.2f), 0, Random.Range(-0.2f, 0.2f));

            // �ϥ� Impulse �Ҧ��A�X�o�������Q�X���ĪG
            elementRb.AddForce(forceDirection.normalized * popForce, ForceMode.Impulse);

            Debug.Log("�\\�l�w�X�l�AcolorElement �Q�X�I");
        }
        else
        {
            Debug.LogWarning("colorElement ���W�S�� Rigidbody�A�L�k�Q�X�I");
        }
    }

    // ���Ѥ@�ӭ��]��k�A�p�G����n���s���@��
    public void ResetTrigger()
    {
        hasTriggered = false;
        colorElement.SetActive(false);
    }
}
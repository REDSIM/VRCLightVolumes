
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class RTUpdater : UdonSharpBehaviour {

    public CustomRenderTexture[] RTs;

    void Start() {
        for (int i = 0; i < RTs.Length; i++) {
            RTs[i].Initialize();
        }
    }

    private void Update() {
        for (int i = 0; i < RTs.Length; i++) {
            RTs[i].Update();
        }
    }

}

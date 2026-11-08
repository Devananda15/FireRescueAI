// FireRescueAI — FireNode.cs
// Data class representing fire intensity in one room.

namespace FireRescueAI.Fire
{
    public class FireNode
    {
        public int   roomId;
        public float intensity;  // 0 = none, 1 = fully engulfed
        public bool  IsActive => intensity > 0.05f;

        public FireNode(int roomId, float startIntensity = 0f)
        {
            this.roomId    = roomId;
            this.intensity = startIntensity;
        }

        public void Increase(float amount)
            => intensity = UnityEngine.Mathf.Clamp01(intensity + amount);

        public void Decrease(float amount)
            => intensity = UnityEngine.Mathf.Clamp01(intensity - amount);
    }
}

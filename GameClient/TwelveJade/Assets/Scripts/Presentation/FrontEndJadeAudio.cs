using UnityEngine;
namespace TwelveJade.Presentation
{
    public sealed partial class FrontEndController
    {
        AudioClip jadeChime;
        void PlayJadeChime()
        {
            if (effects == null) return;
            if (jadeChime == null)
            {
                const int rate = 22050; var samples = new float[(int)(rate * .115f)];
                for (int i = 0; i < samples.Length; i++)
                {
                    float t = i / (float)rate, tail = 1f - i / (float)samples.Length;
                    float tone = Mathf.Sin(2*Mathf.PI*1480*t) + .32f*Mathf.Sin(2*Mathf.PI*2383*t) + .14f*Mathf.Sin(2*Mathf.PI*3700*t);
                    samples[i] = tone * Mathf.Min(1,t*1400) * Mathf.Exp(-t*23) * tail*tail * .16f;
                }
                jadeChime = AudioClip.Create("Jade chime (original synthesis)", samples.Length, 1, rate, false);
                jadeChime.SetData(samples,0);
            }
            effects.PlayOneShot(jadeChime);
        }
    }
}

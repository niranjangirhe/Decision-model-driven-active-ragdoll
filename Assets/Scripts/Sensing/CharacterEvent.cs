using UnityEngine;

namespace ActiveRagdoll
{
    public enum EventKind
    {
        /// <summary>An object is predicted to hit the body soon (anticipation).</summary>
        Incoming,
        /// <summary>An object has hit the body (collision impulse).</summary>
        Impact,
        /// <summary>A foot hit an obstacle while walking.</summary>
        Trip,
        /// <summary>Both feet lost ground contact unexpectedly while standing.</summary>
        FootingLost,
    }

    public enum CharacterState { Standing, Sitting, Walking, Lying }

    /// <summary>
    /// Everything the decision layer gets to know about one event, as numbers.
    /// <see cref="EventSerializer"/> turns it into the English sentence the decision model reads;
    /// the rules selector reads the numbers directly.
    /// Directions are in the character's heading frame: x = right, y = forward.
    /// </summary>
    public class CharacterEvent
    {
        public EventKind Kind;
        public double SimTime;
        public CharacterState State;

        /// <summary>Link name that is (or will be) hit, e.g. "Head", "UpperArm_L".</summary>
        public string BodyPart;
        /// <summary>Horizontal direction the object travels in (heading frame). Zero if it falls straight down.</summary>
        public Vector2 TravelDir;
        /// <summary>True if the object comes mostly from above (falling).</summary>
        public bool FromAbove;

        // Impact
        public float ImpulseNs;

        // Incoming
        public float TimeToImpact;
        public float ObjectMassKg;
        public float ObjectSpeed;
        /// <summary>Horizontal offset (m, heading frame) of the object from the pelvis when sensed.</summary>
        public Vector2 ObjectOffset;
        public int ObjectId;

        // Surroundings: free distance (m) to the nearest obstacle in each heading direction, capped at SurroundRange.
        public float FreeLeft, FreeRight, FreeFront, FreeBack;
        public const float SurroundRange = 2.0f;

        /// <summary>Name of a graspable support within reach, or null.</summary>
        public string SupportName;
        /// <summary>Direction to the support (heading frame).</summary>
        public Vector2 SupportDir;

        public bool HitsHead => BodyPart == "Head";
    }
}

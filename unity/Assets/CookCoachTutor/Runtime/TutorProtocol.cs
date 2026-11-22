using System;

namespace CookCoach.Tutoring
{
    [Serializable] public class KitchenSnapshot
    {
        public string[] completed_steps = Array.Empty<string>();
        public bool stove_on;
        public int related_error_count;
        public string current_step, current_step_title;
        public int step_number, step_count;
    }

    [Serializable] public class KitchenEvent
    {
        public string action_id, outcome, reason;
    }

    [Serializable] public class TutorTurn
    {
        public string role, content;
        public TutorTurn(string role, string content) { this.role = role; this.content = content; }
    }

    [Serializable] public class KitchenContext
    {
        public string session_id, recipe_id, recipe_version, recipe_title;
        public int state_version;
        public KitchenSnapshot snapshot;
        public KitchenEvent[] recent_events = Array.Empty<KitchenEvent>();
    }

    [Serializable] public class TutorRequest
    {
        public string request_id, session_id, recipe_id, recipe_version, trigger, user_question;
        public int state_version;
        public KitchenSnapshot snapshot;
        public KitchenEvent[] recent_events;
        public TutorTurn[] conversation;
        public string[] allowed_highlight_targets;
    }

    [Serializable] public class Advice
    {
        public string message, hint_level, highlight_target_id;
    }

    [Serializable] public class TutorReply
    {
        public string request_id, session_id, source;
        public int state_version;
        public Advice guidance;
    }
}

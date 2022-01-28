using System.Text;

namespace VeloTimer.Shared.Data.Parameters
{
    public class StatisticsParameters
    {
        private string label = string.Empty;
        private string track = string.Empty;
        private string layout = string.Empty;

        public string OrderBy { get; set; } = "passingtime:desc";

        public string Label
        {
            get => label; set
            {
                layout = null;
                track = null;
                label = value;
            }
        }
        public string Track
        {
            get => track; set
            {
                layout = null;
                track = value;
            }
        }
        public string Layout { get => layout; set => layout = value; }

        public string ToQueryString()
        {
            var sb = new StringBuilder(Label);
            if (!string.IsNullOrWhiteSpace(Track)) sb.Append('&').Append(Track);
            if (!string.IsNullOrWhiteSpace(Layout)) sb.Append('&').Append(Layout);

            return sb.ToString();
        }

        public string ToPathString()
        {
            var sb = new StringBuilder(Label);
            if (!string.IsNullOrWhiteSpace(Track)) sb.Append('/').Append(Track);
            if (!string.IsNullOrWhiteSpace(Layout)) sb.Append('/').Append(Layout);

            return sb.ToString();
        }
    }
}

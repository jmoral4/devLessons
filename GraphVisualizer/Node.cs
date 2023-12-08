namespace GraphVisualizer
{
    public class Node
    {
        public enum State
        {
            Unvisited,
            Visited,
            Current,
            Blocked  // New state for blocked nodes 
        }
        public double GCost { get; set; }  // Cost from start to current node
        public double HCost { get; set; }  // Heuristic cost from current node to end node
        public double FCost { get { return GCost + HCost; } }  // Total cost


        public bool IsEndNode { get; set; } = false;
        public bool IsStartNode { get; set; } = false;
        public Node Parent { get; set; }

        public State NodeState { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public Color Color { get; set; }
        public List<Node> AdjacentNodes { get; set; }
        // ... other properties ...
    }

}
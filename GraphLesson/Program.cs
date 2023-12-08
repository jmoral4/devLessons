namespace GraphLesson
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Graph Lesson Launched!");
            /*

            Let's consider this graph:
                    1  
                   / \  
                  2   3  
                     / \  
                    4   5  
                   / \  
                  6   7                                                  
                 /  
                8   


              */

            Console.WriteLine("DFS Output:");



            Console.WriteLine("BFS Output:");


        }
    }


    public class Node
    {
        public int Value;
        public List<Node> Neighbors;
        public bool Visited;

        public Node(int value)
        {
            Value = value;
            Neighbors = new List<Node>();
        }
    }

    public class Graph
    {
        public void DFS(Node node) { 
        
        }

        public void BFS(Node node) { 
        
        }
    }

}
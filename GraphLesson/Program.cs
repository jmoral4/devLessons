using System.ComponentModel.Design;

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

            //setup graph using Nodes


            // our algorithms class
            Graph graph = new Graph();

            Console.WriteLine("DFS Output:");
            // execute DFS and display output
            // call with your first node:   graph.DFS(node1);

            Console.WriteLine("BFS Output:");
            // execute BFS and display output
            // call with your first node:   graph.BFS(node1);
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

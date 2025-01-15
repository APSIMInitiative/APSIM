import zmq
import msgpack
import subprocess
import psutil
import time
import pandas as pd

# Set up a listening server on a random port
def test_proto2(apsim_dir):
    apsim = {}
    
    # The simulation will connect back to this port:
    context = zmq.Context()
    apsim['apsim_socket'] = context.socket(zmq.REP)
    apsim['apsim_socket'].bind("tcp://0.0.0.0:0")
    apsim['random_port'] = apsim['apsim_socket'].getsockopt(zmq.LAST_ENDPOINT).decode().split(":")[-1]
	
    print("Listening on", apsim['apsim_socket'].getsockopt(zmq.LAST_ENDPOINT))
    
    apsim['process'] = subprocess.Popen([
        "/usr/bin/dotnet",
        f"{apsim_dir}/bin/Debug/net8.0/ApsimZMQServer.dll",
        "-p", apsim['random_port'],
        "-P", "interactive",
        "-f", f"{apsim_dir}/Tests/Simulation/ZMQ-Sync/ZMQ-sync.apsimx"
    ])
    
    print("Started Apsim process id", apsim['process'].pid)
    
    return apsim

# Send a command, eg resume/set/get
def send_command(socket, command, args=None):
    socket.send_string(command, zmq.SNDMORE if args else 0)
    if args:
        for i, arg in enumerate(args):
            socket.send(msgpack.packb(arg), zmq.SNDMORE if i < len(args) - 1 else 0)

# The response loop. When the simulation connects we tell it what to do.
# connect -> ok
# paused -> resume/get/set
# finished -> ok
def poll_zmq2(socket):
    while True:
        msg = socket.recv_string()
        if msg == "connect":
            send_command(socket, "ok")
        elif msg == "paused":
            send_command(socket, "get", ["[Clock].Today.Day"])
            reply = msgpack.unpackb(socket.recv())
            assert isinstance(reply, int)
            
            send_command(socket, "get", ["[Wheat].Phenology.Zadok.Stage"])
            reply = msgpack.unpackb(socket.recv())
            # assert isinstance(reply, float)
            
            send_command(socket, "get", ["[Soil].Water.PAW"])
            reply = msgpack.unpackb(socket.recv())
            assert isinstance(reply, list) and len(reply) == 7

            send_command(socket, "get", ["[Nutrient].NO3.kgha"])
            reply = msgpack.unpackb(socket.recv())
            # print(reply)

            send_command(socket, "set", ["[Nutrient].NO3.kgha", [2*ele for ele in reply]])
            msg = socket.recv_string()
            
            send_command(socket, "get", ["[Nutrient].NO3.kgha"])
            reply = msgpack.unpackb(socket.recv())
            # print(reply)
            
            send_command(socket, "get", ["[Manager].Script.DummyStringVar"])
            reply1 = msgpack.unpackb(socket.recv())
            
            send_command(socket, "set", ["[Manager].Script.DummyStringVar", "Blork"])
            msg = socket.recv_string()
            
            send_command(socket, "get", ["[Manager].Script.DummyStringVar"])
            reply2 = msgpack.unpackb(socket.recv())
            assert reply1 != reply2
            
            send_command(socket, "set", ["[Manager].Script.DummyStringVar", reply1])
            msg = socket.recv_string()
            
            send_command(socket, "set", ["[Manager].Script.DummyDoubleVar", 42.42])
            msg = socket.recv_string()
            
            send_command(socket, "resume")
        elif msg == "finished":
            send_command(socket, "ok")
            break

def close_zmq2(apsim):
    apsim['apsim_socket'].close()
    apsim['process'].terminate()
    # process = psutil.Process(apsim['process'].pid)
    # for proc in process.children(recursive=True):
    #     proc.kill()
    # process.kill()

apsim_dir = "/home/trs07170/Research/Pan/ApsimX" 
apsim = test_proto2(apsim_dir)
rec = {"iter":[], "mem":[], "time":[]}

for i in range(10):
    start_time = time.time()
    poll_zmq2(apsim['apsim_socket'])
    proc = psutil.Process(apsim['process'].pid)
    mem_info = proc.memory_info().rss
    elapsed_time = time.time() - start_time
    rec["iter"].append(i + 1)
    rec["mem"].append(mem_info)
    rec["time"].append(elapsed_time)

close_zmq2(apsim)
print(pd.DataFrame(rec))
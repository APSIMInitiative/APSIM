# -*- coding: utf-8 -*-
"""OASIS Apsim Python client.

Welcome to the Python client for the OASIS project!! This module communicates
with a corresponding Apsim client via a ZMQ server. 

Example:
    $ python3 ZMQ-InteractiveVariables.py

Todo:
    * Unify Field nodes in Apsim and this client.
"""

import zmq
import msgpack
import time

import matplotlib.pyplot as plt
    
import pdb

# iteration that rain happens on
RAIN_DAY = 90
rain_day_ts = 0


class ZMQServer(object):
    """Object for interacting with the OASIS 0MQ server.
    
    Attributes:
        port (str):

    """

    def __init__(
            self,
            port: str
        ):
        """
        """
        pass

    def __del__(self):
        """Handles cleanup of protocol"""
        # TODO implement cleanup of protocol, something to restart sim
        self.close_socket()
    
    def open_socket(self):
        """Opens socket to apsim ZQM synchronizer
       
        Should not be called externally. Uses self.conn_str 
        """
        # connect to server
        self.context = zmq.Context.instance()
        print(f"Connecting to server @ {self.conn_str}...")
        self.socket = self.context.socket(zmq.REP)
        self.socket.bind(self.conn_str)
        print('    ...connected.')    
    
    def close_socket(self):
        """Closes connection to apsim server"""
        self.socket.disconnect(self.conn_str)
        self.context.destroy()



class FieldNode(object):
    """A single Field, corresponding to a node in the OASIS sim.
    
    Create a new Field that is linked to its instance in APSIM.
    
    Attributes:
        ap_id (int): Location within Apsim list.
        configs (dict):
        
    """

    def __init__(
            self,
            socket,
            configs: dict = {}
        ):
        """
        Args:
            configs (:obj: `dict`, optional): Pre-formatted field configs; see
                [example_url.com] for supported configurations.
        """
        self.ap_id = None
        self.configs = configs
        self.socket = socket
        self.create()

    def _digest_configs(
            self,
            configs: dict
        ):
        pass

    def _format_configs(self):
        """Prepare FieldNode configs for creating a new Field in Apsim.
        Returns:
            csv_configs (:obj:`list` of :obj:`str`): List of comma-separated
                key-value pairs for each configuration.
        """
        return ["{},{}".format(key, val) for key, val in self.configs.items()]

    def create(self):
        """Create a new field and link with ID reference returned by Apsim."""
        csv_configs = self._format_configs()
        self.id = self.send("")

    def send(
            self,
            command : str,
            args : tuple = None,
            unpack : bool = True
        ):
        """
        Args:
            command (str):
            args (:obj:`list` of :obj:`str`):
            unpack (bool):

        Returns:
            ap_id (int): Corresponds to the index of the node in Apsim.
        """
        pass



class ApsimController:
    """Controller for apsim server"""
    
    def __init__(self, addr="0.0.0.0", port=27746, fields=1):
        """Initializes a ZMQ connection to the Apsim synchronizer
       
        Starts the command sequence by checking connect was received and sending
        "ok" back.
        
        Args:
            addr (str): Server address
            port (str): Server port number
            fields (int): Number of fields to instantiate
        """
        self.fields = fields

        # connection string
        self.conn_str = f"tcp://{addr}:{port}"
        self.open_socket()
        
        # initialize the connection protocol
        msg = self.socket.recv_string()
        print(f"recv msg: {msg}")
        
        if msg != "connect":
            # TODO error handling
            pass
            
        msg = self.send_command("ok", unpack=False)
        
        if msg != "setup":
            # TODO error handling
            pass
        
        msg = self.send_command(
            "field", [
                "Name,CoolField",
                "X,1.0",
                "Y,2.0",
                "Z,3.0"
            ],
            unpack=False
        )
        msg = self.send_command(
            "field", [
                "Name,FreshField",
                "X,4.0",
                "Y,5.0",
                "Z,6.0"
            ],
            unpack=False
        )
        # create fields
        #msg = self.send_command("fields", [self.fields], unpack=False)
        # Begin the simulation.
        msg = self.send_command("energize", [], unpack=False)

    def __del__(self):
        """Handles cleanup of protocol"""
        # TODO implement cleanup of protocol, something to restart sim
        self.close_socket()
    
    def open_socket(self):
        """Opens socket to apsim ZQM synchronizer
       
        Should not be called externally. Uses self.conn_str 
        """
        # connect to server
        self.context = zmq.Context.instance()
        print(f"Connecting to server @ {self.conn_str}...")
        self.socket = self.context.socket(zmq.REP)
        self.socket.bind(self.conn_str)
        print('    ...connected.')    
    
    def close_socket(self):
        """Closes connection to apsim server"""
        self.socket.disconnect(self.conn_str)
        self.context.destroy()

    def send_command(
            self,
            command : str,
            args : tuple = None,
            unpack : bool = True
        ):
        """Sends a string command and optional arguments to the server

        The objects in args can be any serializable type. Internally uses
        msgpack.packb for serialization.

        Args:
            socket: Opened ZMQ created socket
            command: Command string
            args: List of arguments
            unpack: Unpack the resulting bytes
            
        Returns:
            Bytes received from the server. If unpack is True, then the data is
            decoded in a python object. Else if unpack is False the raw bytes is
            returned.
        """
        
        # check for arguments, otherwise just send string
        if args:
            self.socket.send_string(command, flags=zmq.SNDMORE)
            # list of data to send
            msg = []
            # loop over them; serialize using msgpack.
            [msg.append(msgpack.packb(arg)) for arg in args]
            self.socket.send_multipart(msg)
        else:
            self.socket.send_string(command)
            
        # get the response
        recv_bytes = self.socket.recv()
        # process based on arg
        if unpack:
            recv_data = msgpack.unpackb(recv_bytes)
        else:
            recv_data = recv_bytes

        # return data
        return recv_data
    
    def step(self) -> bool:
        """Steps the simulation to the next timestep
        
        Returns:
            Status if simulation has finished. True if simulation is done. False
            if it is still running and can be stepped.
            
        Raises:
            ValueError: When a response the server doesn't match a expected string
        """

        rc = None 
        
        resp_bytes = self.send_command("resume", unpack=False)
        resp = resp_bytes.decode("utf-8")
        if resp == "paused":
            rc = False
        elif resp == "finished":
            rc = True
        else:
            raise ValueError
        return rc


def poll_zmq(controller : ApsimController) -> tuple:
    """Runs the simulation and obtains simulated data

    Implements a response loop to control the simulation and transfer data. The
    sequence of commands in the format (server -> client) are as follows:
        connect     -> ok
        paused      -> resume/get/set/do
        finished    -> ok

    Args:
        controller: Reference to apsim controller

    Returns:
        Returns a tuple of (timestamp, extractable soil water (mm), rain)
    """

    ts_arr = []
    esw1_arr = []
    esw2_arr = []
    rain_arr = []

    counter = 0
    
    running = True

    while (running):
        ts = controller.send_command("get", ["[Clock].Today"])
        ts_arr.append(ts.to_unix())
        
        sw1 = controller.send_command("get", ["sum([CoolField].HeavyClay.Water.Volumetric)"])
        esw1_arr.append(sw1)
        
        sw2 = controller.send_command("get", ["sum([FreshField].HeavyClay.Water.Volumetric)"])
        esw2_arr.append(sw2)

        rain = controller.send_command("get", ["[Weather].Rain"])
        rain_arr.append(rain)

        if counter == RAIN_DAY:
            print("Applying irrigation")
            controller.send_command(
                "do",
                ["applyIrrigation", "amount", 204200.0, "field", 0],
                unpack=False
                )
            global rain_day_ts
            rain_day_ts = ts.to_unix()

        # resume simulation until next ReportEvent
        running = not controller.step()

        # increment counter
        counter += 1

    return (ts_arr, esw1_arr, esw2_arr, rain_arr)


if __name__ == '__main__':
    # initialize connection
    apsim = ApsimController(fields=50) 

    ts_arr, esw1_arr, esw2_arr, rain_arr = poll_zmq(apsim)

    # Code for testing with echo server
    #start_str = socket.recv_string()
    #if start_str != "connect":
    #    raise ValueError(f"Did not get correct start starting, got {start_str}, expected 'connect'")

    #sendCommand(socket, "get", ["[Manager].Script.cumsumfert"])

    #print('Do we get a reply?')
    #reply = socket.recv_multipart()
    #print(reply)

    # make plot
    plt.figure()

    plt.plot(ts_arr, esw1_arr)
    plt.plot(ts_arr, esw2_arr)
    plt.xlabel("Time (Unix epochs)")
    plt.ylabel("Volumetric Water Content")
    plt.axvline(x=rain_day_ts, color='red', linestyle='--', label="Rain day")

    plt.twinx()
    plt.plot(ts_arr, rain_arr, color="orange")
    plt.ylabel("Rain")

    plt.legend()
    plt.grid(True)
    plt.show()
    """
    Test for FieldNode setup.
    patonfigs = {
            "Name": "pato",
            "Bird": "duck"
        }
    pato = FieldNode(configs=patonfigs)
    """
